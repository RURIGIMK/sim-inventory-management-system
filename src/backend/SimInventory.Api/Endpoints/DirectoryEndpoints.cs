using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimInventory.Api.Data;
using SimInventory.Api.Integration;

namespace SimInventory.Api.Endpoints;

/// <summary>
/// HR directory integration. The employee directory stays read-only in the UI; people enter it
/// only from the HR system of record, through one of three adapters that share one sync engine:
///   1. CSV upload of an HR export (administrator),
///   2. pull from the HR API (administrator button, or the scheduled hr-sync workflow),
///   3. signed push from the HR system (webhook), for near real-time joiner/leaver updates.
/// </summary>
public static class DirectoryEndpoints
{
    private const int MaxBodyBytes = 2 * 1024 * 1024;

    public static void MapDirectoryEndpoints(this WebApplication app)
    {
        app.MapPost("/api/admin/employees/import", async (HttpRequest request, ClaimsPrincipal principal,
            SimInventoryDbContext db, string? mode, bool? allowLargeDeactivation, CancellationToken ct) =>
        {
            var csv = await ReadBodyAsync(request, ct);
            if (csv is null) return Results.BadRequest(new { message = "Send the HR export as CSV text up to 2 MB." });
            return await RunAsync(() =>
            {
                var payload = EmployeeDirectorySync.FromCsv(csv, mode ?? "snapshot") with { AllowLargeDeactivation = allowLargeDeactivation };
                return EmployeeDirectorySync.ApplyAsync(db, payload, "csv_import", principal, ct);
            });
        }).RequireAuthorization("Administrators");

        app.MapPost("/api/admin/employees/sync", async (ClaimsPrincipal principal, SimInventoryDbContext db,
            HrDirectoryClient hr, CancellationToken ct) =>
        {
            if (!hr.IsConfigured)
                return Results.Conflict(new { message = "The HR directory API is not configured yet (Hr:DirectoryUrl). Use CSV import until the HR integration is connected." });
            return await RunAsync(async () =>
                await EmployeeDirectorySync.ApplyAsync(db, await hr.FetchSnapshotAsync(ct), "hr_api", principal, ct));
        }).RequireAuthorization("Administrators");

        app.MapGet("/api/admin/employees/sync-runs", async (SimInventoryDbContext db, HrDirectoryClient hr, IConfiguration configuration) =>
        {
            var runs = await db.EmployeeSyncRuns.AsNoTracking().OrderByDescending(x => x.Id).Take(20).ToListAsync();
            return Results.Ok(new
            {
                pullConfigured = hr.IsConfigured,
                webhookConfigured = !string.IsNullOrWhiteSpace(configuration["Hr:WebhookSecret"]),
                runs = runs.Select(r => new
                {
                    r.Id, r.Source, r.Mode, r.StartedAt, r.FinishedAt, r.Received, r.Created, r.Updated,
                    r.Deactivated, r.Unchanged, r.Rejected, r.Outcome, r.TriggeredBy,
                    errors = JsonSerializer.Deserialize<List<string>>(r.ErrorsJson)
                })
            });
        }).RequireAuthorization("Administrators");

        // Scheduled pull, called by the hr-sync GitHub Actions workflow with the integration token.
        app.MapPost("/api/integrations/hr/sync", async (SimInventoryDbContext db, HrDirectoryClient hr, CancellationToken ct) =>
        {
            if (!hr.IsConfigured)
                return Results.Conflict(new { message = "Hr:DirectoryUrl is not configured." });
            return await RunAsync(async () => await EmployeeDirectorySync.ApplyAsync(db, await hr.FetchSnapshotAsync(ct),
                "hr_api", IntegrationTokenFilter.Actor("hr-sync"), ct));
        }).AddEndpointFilter<IntegrationTokenFilter>();

        // Push from the HR system. Authenticated by HMAC signature, not by user session.
        app.MapPost("/api/integrations/hr/employees", async (HttpRequest request, SimInventoryDbContext db,
            IConfiguration configuration, CancellationToken ct) =>
        {
            var secret = configuration["Hr:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
                return Results.Problem("The HR webhook is not configured (Hr:WebhookSecret).", statusCode: 503);
            var body = await ReadBodyAsync(request, ct);
            if (body is null) return Results.BadRequest(new { message = "Body is missing or larger than 2 MB." });
            if (!HrWebhookSignature.IsValid(secret, request.Headers[HrWebhookSignature.TimestampHeader],
                    request.Headers[HrWebhookSignature.SignatureHeader], body, DateTimeOffset.UtcNow))
                return Results.Unauthorized();
            HrDirectoryPayload? payload;
            try { payload = JsonSerializer.Deserialize<HrDirectoryPayload>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch (JsonException) { return Results.BadRequest(new { message = "Body is not valid JSON." }); }
            if (payload?.Employees is null) return Results.BadRequest(new { message = "employees is required." });
            return await RunAsync(() => EmployeeDirectorySync.ApplyAsync(db, payload, "hr_webhook",
                IntegrationTokenFilter.Actor("hr-webhook"), ct));
        }).RequireRateLimiting("integration");
    }

    private static async Task<IResult> RunAsync(Func<Task<SyncOutcome>> action)
    {
        try
        {
            var outcome = await action();
            var run = outcome.Run;
            return Results.Ok(new
            {
                run = new
                {
                    run.Id, run.Source, run.Mode, run.Received, run.Created, run.Updated, run.Deactivated,
                    run.Unchanged, run.Rejected, run.Outcome
                },
                errors = JsonSerializer.Deserialize<List<string>>(run.ErrorsJson),
                holdersNeedingRecovery = outcome.HoldersNeedingRecovery
            });
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return Results.Json(new { message = "The HR directory could not be reached: " + ex.Message }, statusCode: 502);
        }
    }

    internal static async Task<string?> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength is > MaxBodyBytes) return null;
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var buffer = new char[MaxBodyBytes + 1];
        var builder = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            builder.Append(buffer, 0, read);
            if (builder.Length > MaxBodyBytes) return null;
        }
        return builder.Length == 0 ? null : builder.ToString();
    }
}
