using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SimInventory.Api.Data;
using SimInventory.Api.Integration;
using SimInventory.Api.Models;
using Xunit;

namespace SimInventory.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class ApiBehaviourTests(ApiFixture api)
{
    [Fact]
    public async Task ConcurrentAllocationsOfOneSimProduceExactlyOneAssignment()
    {
        var (_, admin) = await api.UserAsync(UserRole.Administrator);
        var sim = await api.SimAsync();
        var employees = new List<Employee>();
        for (var i = 0; i < 8; i++) employees.Add(await api.EmployeeAsync("Operations"));

        var responses = await Task.WhenAll(employees.Select(e =>
            admin.PostAsJsonAsync("/api/allocations", new { simId = sim.Id, employeeId = e.Id })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await api.WithDbAsync(db => db.Assignments.CountAsync(a => a.SimId == sim.Id && a.ReturnedAt == null)));
    }

    [Fact]
    public async Task AllocationAndReassignmentAuditEventsRecordTheRealAssignmentId()
    {
        var (_, admin) = await api.UserAsync(UserRole.Administrator);
        var sim = await api.SimAsync();
        var first = await api.EmployeeAsync("Finance");
        var second = await api.EmployeeAsync("Finance");

        var allocated = await (await admin.PostAsJsonAsync("/api/allocations", new { simId = sim.Id, employeeId = first.Id }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var assignmentId = allocated.GetProperty("id").GetInt32();
        var reassigned = await (await admin.PostAsJsonAsync($"/api/allocations/{assignmentId}/reassign", new { employeeId = second.Id }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var newId = reassigned.GetProperty("id").GetInt32();

        var events = await api.WithDbAsync(db => db.AuditEvents.Where(e => e.EntityType == "assignment").ToListAsync());
        Assert.Contains(events, e => e.Action == "sim_allocated" && e.EntityId == assignmentId.ToString());
        Assert.Contains(events, e => e.Action == "sim_reassigned" && e.EntityId == newId.ToString());
        Assert.DoesNotContain(events, e => e.EntityId == "0");
    }

    [Theory]
    [InlineData(UserRole.DepartmentalManager, "/api/admin/users", "GET", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.DepartmentalManager, "/api/admin/audit", "GET", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.DepartmentalManager, "/api/sims", "POST", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Viewer, "/api/allocations", "POST", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Viewer, "/api/admin/billing/import", "POST", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Viewer, "/api/sims", "GET", HttpStatusCode.OK)]
    [InlineData(UserRole.DepartmentalManager, "/api/intelligence/anomalies", "GET", HttpStatusCode.OK)]
    public async Task RolesAreEnforcedByTheApi(UserRole role, string path, string method, HttpStatusCode expected)
    {
        var (_, client) = await api.UserAsync(role, role == UserRole.DepartmentalManager ? "Finance" : null);
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task DepartmentalManagerCanOnlyManageCustodyInTheirDepartment()
    {
        var (_, manager) = await api.UserAsync(UserRole.DepartmentalManager, "Finance");
        var sim = await api.SimAsync();
        var outsider = await api.EmployeeAsync("Operations");
        var insider = await api.EmployeeAsync("Finance");

        var denied = await manager.PostAsJsonAsync("/api/allocations", new { simId = sim.Id, employeeId = outsider.Id });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var allowed = await manager.PostAsJsonAsync("/api/allocations", new { simId = sim.Id, employeeId = insider.Id });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        var id = (await allowed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var outward = await manager.PostAsJsonAsync($"/api/allocations/{id}/reassign", new { employeeId = outsider.Id });
        Assert.Equal(HttpStatusCode.Forbidden, outward.StatusCode);
    }

    [Fact]
    public async Task DisabledAccountIsRejectedEvenWithAValidToken()
    {
        var (user, client) = await api.UserAsync(UserRole.Administrator);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        await api.WithDbAsync(async db =>
        {
            var stored = await db.Users.SingleAsync(x => x.Id == user.Id);
            stored.IsActive = false;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task SimLifecycleRulesAreEnforced()
    {
        var (_, admin) = await api.UserAsync(UserRole.Administrator);
        var sim = await api.SimAsync();
        var employee = await api.EmployeeAsync("Finance");

        var suspend = await admin.PostAsJsonAsync($"/api/sims/{sim.Id}/status", new { status = "Suspended", reason = "Reported lost" });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/allocations", new { simId = sim.Id, employeeId = employee.Id })).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/sims/{sim.Id}/status", new { status = "Available", reason = "Found" })).StatusCode);
        var allocation = await admin.PostAsJsonAsync("/api/allocations", new { simId = sim.Id, employeeId = employee.Id });
        Assert.Equal(HttpStatusCode.Created, allocation.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/sims/{sim.Id}/status", new { status = "Deactivated", reason = "End of contract" })).StatusCode);

        var id = (await allocation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await admin.PostAsync($"/api/allocations/{id}/return", null);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/sims/{sim.Id}/status", new { status = "Deactivated", reason = "End of contract" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/sims/{sim.Id}/status", new { status = "Available", reason = "Try again" })).StatusCode);

        var update = await admin.PutAsJsonAsync($"/api/sims/{sim.Id}", new
        {
            @operator = "Airtel", planType = "Data", monthlyCost = 1.0m, contractStart = (string?)null, contractEnd = (string?)null, costCentre = "X"
        });
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
    }

    [Fact]
    public async Task DuplicateSimIdentityReturnsConflict()
    {
        var (_, admin) = await api.UserAsync(UserRole.Administrator);
        var existing = await api.SimAsync();
        var response = await admin.PostAsJsonAsync("/api/sims", new
        {
            @operator = "Airtel", phoneNumber = existing.PhoneNumber, iccid = "NEW-" + Guid.NewGuid().ToString("N")[..8],
            planType = "Data", monthlyCost = 500m, costCentre = "CC"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task BillingImportIsAllOrNothingAndNeverSilentlyOverwrites()
    {
        var (_, admin) = await api.UserAsync(UserRole.Administrator);
        var sim = await api.SimAsync(1000m);
        var bad = $"iccid,period,amount\n{sim.Iccid},2026-01,1000\nUNKNOWN,2026-01,10\n";
        var badResponse = await admin.PostAsync("/api/admin/billing/import", new StringContent(bad, Encoding.UTF8, "text/csv"));
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);
        Assert.Equal(0, await api.WithDbAsync(db => db.CostRecords.CountAsync(x => x.SimId == sim.Id)));

        var good = $"iccid,period,amount,data_mb,voice_minutes\n{sim.Iccid},2026-01,1000,500,100\n{sim.Iccid},2026-02,2600.50,4000,90\n";
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/billing/import", new StringContent(good, Encoding.UTF8, "text/csv"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/billing/import", new StringContent(good, Encoding.UTF8, "text/csv"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/billing/import?replace=true", new StringContent(good, Encoding.UTF8, "text/csv"))).StatusCode);
        Assert.Equal(2, await api.WithDbAsync(db => db.CostRecords.CountAsync(x => x.SimId == sim.Id)));
    }

    [Fact]
    public async Task HrWebhookRequiresAValidSignatureAndAppliesSnapshots()
    {
        var leaver = await api.EmployeeAsync("Operations");
        var sim = await api.SimAsync();
        var (_, admin) = await api.UserAsync(UserRole.Administrator);
        await admin.PostAsJsonAsync("/api/allocations", new { simId = sim.Id, employeeId = leaver.Id });

        var everyoneElse = await api.WithDbAsync(db => db.Employees.Where(e => e.Status == EmploymentStatus.Active && e.Id != leaver.Id).ToListAsync());
        var payload = JsonSerializer.Serialize(new
        {
            mode = "snapshot",
            employees = everyoneElse.Select(e => new
            {
                employeeNumber = e.EmployeeNumber, fullName = e.FullName, email = e.Email,
                department = e.Department, costCentre = e.CostCentre, status = "active"
            }).Append(new
            {
                employeeNumber = "NEW-" + Guid.NewGuid().ToString("N")[..6], fullName = "Amani Kariuki", email = (string?)null,
                department = "Finance", costCentre = "FIN-021", status = "active"
            })
        });

        using var client = api.Factory.CreateClient();
        var unsigned = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/hr/employees") { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        unsigned.Headers.Add(HrWebhookSignature.TimestampHeader, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        unsigned.Headers.Add(HrWebhookSignature.SignatureHeader, "sha256=00");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(unsigned)).StatusCode);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signed = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/hr/employees") { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        signed.Headers.Add(HrWebhookSignature.TimestampHeader, timestamp);
        signed.Headers.Add(HrWebhookSignature.SignatureHeader, HrWebhookSignature.Sign(ApiFixture.WebhookSecret, timestamp, payload));
        var response = await client.SendAsync(signed);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("run").GetProperty("created").GetInt32());
        Assert.Equal(1, body.GetProperty("run").GetProperty("deactivated").GetInt32());
        Assert.Equal(1, body.GetProperty("holdersNeedingRecovery").GetArrayLength());
        Assert.Equal(EmploymentStatus.Inactive, await api.WithDbAsync(db => db.Employees.Where(e => e.Id == leaver.Id).Select(e => e.Status).SingleAsync()));
    }

    [Fact]
    public async Task SnapshotThatWouldDeactivateMostStaffIsRefused()
    {
        for (var i = 0; i < 5; i++) await api.EmployeeAsync("Procurement");
        var payload = new HrDirectoryPayload("snapshot", [new HrEmployeeRecord("ONLY-1", "One Person", null, "Finance", null, "active")]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.WithDbAsync(db =>
            EmployeeDirectorySync.ApplyAsync(db, payload, "test", IntegrationTokenFilter.Actor("test"))));
    }

    [Fact]
    public async Task IntegrationRoutesRequireTheIntegrationToken()
    {
        using var anonymous = api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/integrations/ml/dataset")).StatusCode);
        using var integration = api.IntegrationClient();
        Assert.Equal(HttpStatusCode.OK, (await integration.GetAsync("/api/integrations/ml/dataset")).StatusCode);
        var (_, admin) = await api.UserAsync(UserRole.Administrator);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/integrations/ml/dataset")).StatusCode);
    }

    [Fact]
    public async Task ModelLoopRegistersScoresActivatesAndCollectsReviews()
    {
        var (_, admin) = await api.UserAsync(UserRole.Administrator);
        var sim = await api.SimAsync(1000m);
        await admin.PostAsync("/api/admin/billing/import",
            new StringContent($"iccid,period,amount,data_mb,voice_minutes\n{sim.Iccid},2026-03,1000,500,100\n{sim.Iccid},2026-04,1000,0,0\n", Encoding.UTF8, "text/csv"));

        using var pipeline = api.IntegrationClient();
        var registered = await pipeline.PostAsJsonAsync("/api/integrations/ml/models", new
        {
            version = "test-" + Guid.NewGuid().ToString("N")[..8], algorithm = "test", trainedAt = DateTimeOffset.UtcNow,
            trainingRows = 2, labelledRows = 0, labelSource = "none", datasetSha256 = "x", features = new[] { "f" },
            metrics = new { evaluated = false }, artifactBase64 = Convert.ToBase64String([1, 2, 3]), gitCommit = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var modelId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var scores = await pipeline.PostAsJsonAsync("/api/integrations/ml/scores", new
        {
            modelVersionId = modelId,
            scores = new[] { new { simId = sim.Id, period = "2026-04", score = 0.3, isFlagged = true, reasons = new[] { "No usage" } } }
        });
        Assert.Equal(HttpStatusCode.OK, scores.StatusCode);

        var shadow = await (await admin.GetAsync("/api/intelligence/anomalies?period=2026-04")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(shadow.GetProperty("model").GetProperty("shadow").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/ml/models/{modelId}/activate", null)).StatusCode);
        var review = await admin.PostAsJsonAsync("/api/intelligence/reviews", new { simId = sim.Id, period = "2026-04", decision = "confirmed", note = "Idle SIM" });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Equal("model", (await review.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("flaggedBy").GetString());

        var view = await (await admin.GetAsync("/api/intelligence/anomalies?period=2026-04")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(view.GetProperty("model").GetProperty("shadow").GetBoolean());
        var item = view.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("simId").GetInt32() == sim.Id);
        Assert.True(item.GetProperty("modelFlag").GetBoolean());
        Assert.False(item.GetProperty("ruleFlag").GetBoolean());
        Assert.Equal("confirmed", item.GetProperty("review").GetProperty("decision").GetString());
    }

    [Fact]
    public async Task AuditEventsCannotBeUpdatedOrDeleted()
    {
        var (_, admin) = await api.UserAsync(UserRole.Administrator);
        await api.SimAsync();
        await admin.PostAsJsonAsync("/api/sims", new
        {
            @operator = "Airtel", phoneNumber = "AUD-" + Guid.NewGuid().ToString("N")[..8], iccid = "AUD-" + Guid.NewGuid().ToString("N")[..8],
            planType = "Data", monthlyCost = 500m, costCentre = "CC"
        });
        await using var connection = new NpgsqlConnection(api.ConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[] { "UPDATE \"AuditEvents\" SET \"Details\" = '{}'", "DELETE FROM \"AuditEvents\"", "TRUNCATE \"AuditEvents\"" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Contains("append-only", error.MessageText);
        }
    }

    [Fact]
    public async Task MigrationsAreRecordedAndRerunningThemChangesNothing()
    {
        var applied = await api.WithDbAsync(db => SchemaMigrator.MigrateAsync(db));
        Assert.Empty(applied);
        var recorded = await api.WithDbAsync(db => db.Database.SqlQueryRaw<string>("SELECT \"Id\" AS \"Value\" FROM \"__SchemaMigrations\"").ToListAsync());
        Assert.Equal(SchemaMigrator.LoadScripts().Select(s => s.Id).OrderBy(x => x), recorded.OrderBy(x => x));
    }
}
