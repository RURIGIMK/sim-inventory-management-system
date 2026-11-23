using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimInventory.Api.Data;
using SimInventory.Api.Integration;
using SimInventory.Api.Models;
using SimInventory.Api.Security;

namespace SimInventory.Api.Endpoints;

/// <summary>
/// Billing history, the transparent cost rule, the machine-learning loop and human review.
///
/// Flow: operator invoice CSV → SimCostRecords → scheduled pipeline reads /api/integrations/ml/dataset,
/// trains a candidate, registers it and publishes scores → an administrator compares the candidate with
/// the rule and the active model and decides whether to activate it → reviewers confirm or dismiss flags
/// → reviews become the labels the next candidate is evaluated against.
/// The model never changes a record and never activates itself.
/// </summary>
public static class CostIntelligenceEndpoints
{
    /// <summary>Transparent baseline. Kept identical to RULE_HIGH/RULE_LOW in src/ml-pipeline.</summary>
    public const decimal RuleHighRatio = 1.5m;
    public const decimal RuleLowRatio = 0.5m;
    private const int MaxArtifactBytes = 20 * 1024 * 1024;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static (bool Flagged, string? Reason) EvaluateRule(decimal amount, decimal contracted)
    {
        if (contracted <= 0) return (false, null);
        var ratio = amount / contracted;
        if (ratio >= RuleHighRatio) return (true, $"Billed {ratio:0.00}× the contracted monthly fee.");
        if (ratio <= RuleLowRatio) return (true, $"Billed only {ratio:0.00}× the contracted monthly fee.");
        return (false, null);
    }

    public static bool TryParsePeriod(string? value, out DateOnly period)
    {
        period = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.Length >= 7 && DateTime.TryParseExact(text[..7], "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed) && parsed.Year >= 2000)
        {
            period = new DateOnly(parsed.Year, parsed.Month, 1);
            return true;
        }
        return false;
    }

    public static string FormatPeriod(DateOnly period) => period.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static void MapCostIntelligenceEndpoints(this WebApplication app)
    {
        MapBilling(app);
        MapIntegration(app);
        MapReviewAndModels(app);
    }

    private static void MapBilling(WebApplication app)
    {
        app.MapPost("/api/admin/billing/import", async (HttpRequest request, ClaimsPrincipal principal,
            SimInventoryDbContext db, bool? replace, CancellationToken ct) =>
        {
            var csv = await DirectoryEndpoints.ReadBodyAsync(request, ct);
            if (csv is null) return Results.BadRequest(new { message = "Send the operator invoice as CSV text up to 2 MB." });
            SimpleCsv.Table table;
            try { table = SimpleCsv.Parse(csv); }
            catch (FormatException ex) { return Results.BadRequest(new { message = ex.Message }); }
            if (!table.Headers.Contains("period") || !table.Headers.Contains("amount") ||
                !(table.Headers.Contains("iccid") || table.Headers.Contains("phone_number")))
                return Results.BadRequest(new { message = "Required columns: iccid or phone_number, period (YYYY-MM), amount. Optional: data_mb, voice_minutes." });

            var sims = await db.Sims.AsNoTracking().ToListAsync(ct);
            var byIccid = sims.ToDictionary(x => x.Iccid, StringComparer.OrdinalIgnoreCase);
            var byPhone = sims.ToDictionary(x => x.PhoneNumber, StringComparer.OrdinalIgnoreCase);
            var currentMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var errors = new List<string>();
            var lines = new Dictionary<(int SimId, DateOnly Period), (decimal Amount, int? Data, int? Voice)>();
            for (var i = 0; i < table.Rows.Count; i++)
            {
                var r = table.Rows[i];
                var label = $"Row {i + 2}";
                Sim? sim = null;
                if (r.TryGetValue("iccid", out var iccid) && !string.IsNullOrWhiteSpace(iccid)) byIccid.TryGetValue(iccid, out sim);
                if (sim is null && r.TryGetValue("phone_number", out var phone) && !string.IsNullOrWhiteSpace(phone)) byPhone.TryGetValue(phone, out sim);
                if (sim is null) { errors.Add($"{label}: SIM not found in the inventory."); continue; }
                if (!TryParsePeriod(r.GetValueOrDefault("period"), out var period) || period > currentMonth)
                { errors.Add($"{label}: period must be YYYY-MM and not in the future."); continue; }
                if (!decimal.TryParse(r.GetValueOrDefault("amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0 || amount > 10_000_000m)
                { errors.Add($"{label}: amount must be a non-negative number."); continue; }
                int? data = null, voice = null;
                if (!string.IsNullOrWhiteSpace(r.GetValueOrDefault("data_mb")))
                {
                    if (!int.TryParse(r["data_mb"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) || d < 0) { errors.Add($"{label}: data_mb must be a whole number."); continue; }
                    data = d;
                }
                if (!string.IsNullOrWhiteSpace(r.GetValueOrDefault("voice_minutes")))
                {
                    if (!int.TryParse(r["voice_minutes"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < 0) { errors.Add($"{label}: voice_minutes must be a whole number."); continue; }
                    voice = v;
                }
                if (!lines.TryAdd((sim.Id, period), (Math.Round(amount, 2), data, voice)))
                    errors.Add($"{label}: the same SIM and period appear more than once in this file.");
            }
            if (lines.Count == 0 && errors.Count == 0) errors.Add("The file contains no invoice lines.");

            var simIds = lines.Keys.Select(k => k.SimId).Distinct().ToArray();
            var existing = await db.CostRecords.Where(x => simIds.Contains(x.SimId)).ToListAsync(ct);
            var existingByKey = existing.ToDictionary(x => (x.SimId, x.Period));
            if (replace != true)
                foreach (var key in lines.Keys.Where(existingByKey.ContainsKey))
                    errors.Add($"SIM {key.SimId} already has a billed amount for {FormatPeriod(key.Period)}. Re-import with replace=true to correct it.");
            if (errors.Count > 0)
                return Results.BadRequest(new { message = "Nothing was imported. Fix these rows and import the whole file again.", errors = errors.Take(100) });

            var actorId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : (Guid?)null;
            var batch = Guid.NewGuid();
            var created = 0; var replaced = 0;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (var ((simId, period), line) in lines)
            {
                if (existingByKey.TryGetValue((simId, period), out var entry))
                {
                    replaced++;
                    entry.Source = "operator_invoice";
                    entry.SyntheticScenario = null;
                }
                else
                {
                    entry = new SimCostRecord { SimId = simId, Period = period, Source = "operator_invoice" };
                    db.CostRecords.Add(entry);
                    created++;
                }
                entry.Amount = line.Amount; entry.DataUsageMb = line.Data; entry.VoiceMinutes = line.Voice;
                entry.ImportBatchId = batch; entry.ImportedByUserId = actorId; entry.ImportedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            await AuditWriter.WriteAsync(db, principal, "billing_imported", "billing_batch", batch.ToString(),
                JsonSerializer.Serialize(new
                {
                    rows = lines.Count, created, replaced, totalAmount = lines.Values.Sum(x => x.Amount),
                    periods = lines.Keys.Select(k => FormatPeriod(k.Period)).Distinct().OrderBy(x => x)
                }));
            await transaction.CommitAsync(ct);
            return Results.Ok(new { batchId = batch, created, replaced });
        }).RequireAuthorization("Administrators");

        app.MapGet("/api/billing/summary", async (SimInventoryDbContext db) =>
        {
            var rows = await db.CostRecords.AsNoTracking()
                .Join(db.Sims.AsNoTracking(), c => c.SimId, s => s.Id, (c, s) => new { c.Period, c.Amount, s.MonthlyCost, c.Source })
                .ToListAsync();
            var periods = rows.GroupBy(x => x.Period).OrderByDescending(g => g.Key).Take(24).Select(g => new
            {
                period = FormatPeriod(g.Key),
                records = g.Count(),
                billed = g.Sum(x => x.Amount),
                contracted = g.Sum(x => x.MonthlyCost),
                ruleFlags = g.Count(x => EvaluateRule(x.Amount, x.MonthlyCost).Flagged),
                synthetic = g.Count(x => x.Source == "synthetic")
            });
            return Results.Ok(new { totalRecords = rows.Count, periods });
        }).RequireAuthorization("Session");

        app.MapGet("/api/sims/{id:int}/costs", async (int id, SimInventoryDbContext db) =>
        {
            var sim = await db.Sims.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (sim is null) return Results.NotFound();
            var costs = await db.CostRecords.AsNoTracking().Where(x => x.SimId == id).OrderBy(x => x.Period).ToListAsync();
            return Results.Ok(costs.Select(c => new
            {
                period = FormatPeriod(c.Period), c.Amount, contracted = sim.MonthlyCost, c.DataUsageMb, c.VoiceMinutes, c.Source
            }));
        }).RequireAuthorization("Session");
    }

    private static void MapIntegration(WebApplication app)
    {
        var integration = app.MapGroup("/api/integrations").AddEndpointFilter<IntegrationTokenFilter>().RequireRateLimiting("integration");

        // Raw, minimised training data. No phone numbers, ICCIDs, names or emails leave the API.
        integration.MapGet("/ml/dataset", async (SimInventoryDbContext db) =>
        {
            var sims = await db.Sims.AsNoTracking().Select(s => new
            {
                s.Id, s.Operator, s.PlanType, contractedMonthlyCost = s.MonthlyCost, status = s.Status.ToString(), s.CostCentre
            }).ToListAsync();
            var costs = await db.CostRecords.AsNoTracking().OrderBy(x => x.SimId).ThenBy(x => x.Period).ToListAsync();
            var assignments = await db.Assignments.AsNoTracking()
                .Select(a => new { a.SimId, a.EmployeeId, a.AssignedAt, a.ReturnedAt }).ToListAsync();
            var employees = await db.Employees.AsNoTracking().Select(e => new { e.Id, status = e.Status.ToString() }).ToListAsync();
            var reviews = await db.AnomalyReviews.AsNoTracking().ToListAsync();
            return Results.Ok(new
            {
                generatedAt = DateTimeOffset.UtcNow,
                rule = new { highRatio = RuleHighRatio, lowRatio = RuleLowRatio },
                sims,
                costs = costs.Select(c => new
                {
                    c.SimId, period = FormatPeriod(c.Period), c.Amount, c.DataUsageMb, c.VoiceMinutes, c.Source, c.SyntheticScenario
                }),
                assignments, employees,
                reviews = reviews.Select(r => new { r.SimId, period = FormatPeriod(r.Period), r.Decision })
            });
        });

        integration.MapGet("/ml/models", async (SimInventoryDbContext db) =>
            Results.Ok(await db.ModelVersions.AsNoTracking().OrderByDescending(x => x.Id)
                .Select(m => new { m.Id, m.Version, m.Status, m.TrainedAt, m.ArtifactSha256 }).ToListAsync()));

        integration.MapGet("/ml/models/{id:int}/artifact", async (int id, SimInventoryDbContext db) =>
        {
            var model = await db.ModelVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            return model is null ? Results.NotFound() : Results.Ok(new
            {
                model.Id, model.Version, model.Status, model.ArtifactSha256,
                features = JsonSerializer.Deserialize<List<string>>(model.FeaturesJson),
                artifactBase64 = Convert.ToBase64String(model.Artifact)
            });
        });

        integration.MapPost("/ml/models", async (RegisterModelRequest request, SimInventoryDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Version) || request.Version.Length > 60)
                return Results.BadRequest(new { message = "version is required (max 60 characters)." });
            byte[] artifact;
            try { artifact = Convert.FromBase64String(request.ArtifactBase64 ?? string.Empty); }
            catch (FormatException) { return Results.BadRequest(new { message = "artifactBase64 is not valid base64." }); }
            if (artifact.Length == 0 || artifact.Length > MaxArtifactBytes)
                return Results.BadRequest(new { message = "The model artifact is empty or larger than 20 MB." });
            if (request.Features is null || request.Features.Count == 0)
                return Results.BadRequest(new { message = "features are required." });
            if (await db.ModelVersions.AnyAsync(x => x.Version == request.Version))
                return Results.Conflict(new { message = $"Model version {request.Version} is already registered." });

            await using var transaction = await db.Database.BeginTransactionAsync();
            // Only one candidate waits for review at a time; older unreviewed candidates are retired, not deleted.
            var olderCandidates = await db.ModelVersions.Where(x => x.Status == ModelStatus.Candidate).ToListAsync();
            foreach (var old in olderCandidates) old.Status = ModelStatus.Retired;
            var model = new ModelVersion
            {
                Version = request.Version.Trim(), Algorithm = request.Algorithm, Status = ModelStatus.Candidate,
                TrainedAt = request.TrainedAt, TrainingRows = request.TrainingRows, LabelledRows = request.LabelledRows,
                LabelSource = request.LabelSource ?? "none", DatasetSha256 = request.DatasetSha256 ?? string.Empty,
                FeaturesJson = JsonSerializer.Serialize(request.Features), MetricsJson = request.Metrics.ValueKind is JsonValueKind.Object ? request.Metrics.GetRawText() : "{}",
                Artifact = artifact, ArtifactSha256 = Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant(),
                GitCommit = request.GitCommit
            };
            db.ModelVersions.Add(model);
            await db.SaveChangesAsync();
            await AuditWriter.WriteAsync(db, IntegrationTokenFilter.Actor("ml-pipeline"), "ml_model_registered", "model_version",
                model.Id.ToString(), JsonSerializer.Serialize(new
                {
                    model.Version, model.Algorithm, model.TrainingRows, model.LabelledRows, model.LabelSource,
                    model.ArtifactSha256, retiredCandidates = olderCandidates.Select(x => x.Version)
                }));
            await transaction.CommitAsync();
            return Results.Created($"/api/ml/models/{model.Id}", new { model.Id, model.Version, model.Status, model.ArtifactSha256 });
        });

        integration.MapPost("/ml/scores", async (PublishScoresRequest request, SimInventoryDbContext db) =>
        {
            if (request.Scores is null || request.Scores.Count == 0 || request.Scores.Count > 20_000)
                return Results.BadRequest(new { message = "Send between 1 and 20000 scores." });
            var model = await db.ModelVersions.SingleOrDefaultAsync(x => x.Id == request.ModelVersionId);
            if (model is null) return Results.NotFound(new { message = "Unknown model version." });
            var simIds = (await db.Sims.Select(x => x.Id).ToListAsync()).ToHashSet();
            var parsedScores = new List<(int SimId, DateOnly Period, ScoreItem Item)>();
            foreach (var s in request.Scores)
            {
                if (!simIds.Contains(s.SimId) || !TryParsePeriod(s.Period, out var period) || double.IsNaN(s.Score) || double.IsInfinity(s.Score))
                    return Results.BadRequest(new { message = $"Invalid score for SIM {s.SimId}, period {s.Period}." });
                parsedScores.Add((s.SimId, period, s));
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            var existing = (await db.AnomalyScores.Where(x => x.ModelVersionId == model.Id).ToListAsync())
                .ToDictionary(x => (x.SimId, x.Period));
            var now = DateTimeOffset.UtcNow;
            foreach (var (simId, period, item) in parsedScores)
            {
                if (!existing.TryGetValue((simId, period), out var score))
                {
                    score = new AnomalyScore { ModelVersionId = model.Id, SimId = simId, Period = period };
                    db.AnomalyScores.Add(score);
                    existing[(simId, period)] = score;
                }
                score.Score = item.Score;
                score.IsFlagged = item.IsFlagged;
                score.ReasonsJson = JsonSerializer.Serialize((item.Reasons ?? []).Take(5).Select(r => r.Length > 200 ? r[..200] : r));
                score.ScoredAt = now;
            }
            await db.SaveChangesAsync();
            await AuditWriter.WriteAsync(db, IntegrationTokenFilter.Actor("ml-pipeline"), "ml_scores_published", "model_version",
                model.Id.ToString(), JsonSerializer.Serialize(new
                {
                    model.Version, scores = parsedScores.Count, flagged = parsedScores.Count(x => x.Item.IsFlagged),
                    periods = parsedScores.Select(x => FormatPeriod(x.Period)).Distinct().Count()
                }));
            await transaction.CommitAsync();
            return Results.Ok(new { model.Version, published = parsedScores.Count });
        });

        // Head of the audit chain, recorded daily outside this database by the audit-anchor workflow.
        integration.MapGet("/audit/head", async (SimInventoryDbContext db) =>
        {
            var events = await db.AuditEvents.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            var valid = AuditChainRecovery.Verify(events, out _, out _, out var invalidEventId);
            var last = events.LastOrDefault();
            return Results.Ok(new
            {
                observedAt = DateTimeOffset.UtcNow, eventCount = events.Count, lastEventId = last?.Id,
                headHash = last?.Hash, chainValid = valid, firstInvalidEventId = invalidEventId
            });
        });
    }

    private static void MapReviewAndModels(WebApplication app)
    {
        app.MapGet("/api/intelligence/anomalies", async (string? period, SimInventoryDbContext db) =>
        {
            var periods = await db.CostRecords.AsNoTracking().Select(x => x.Period).Distinct()
                .OrderByDescending(x => x).ToListAsync();
            var model = await db.ModelVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Status == ModelStatus.Active)
                ?? await db.ModelVersions.AsNoTracking().Where(x => x.Status == ModelStatus.Candidate)
                    .OrderByDescending(x => x.Id).FirstOrDefaultAsync();
            object? modelInfo = model is null ? null : new
            {
                model.Id, model.Version, model.Status, model.Algorithm, model.TrainedAt, model.TrainingRows,
                model.LabelledRows, model.LabelSource, shadow = model.Status != ModelStatus.Active,
                metrics = JsonDocument.Parse(model.MetricsJson).RootElement
            };
            if (periods.Count == 0)
                return Results.Ok(new { periods = Array.Empty<string>(), selectedPeriod = (string?)null, model = modelInfo, items = Array.Empty<object>(), performance = (object?)null });

            DateOnly selected;
            if (!TryParsePeriod(period, out selected) || !periods.Contains(selected)) selected = periods[0];

            var rows = await db.CostRecords.AsNoTracking().Where(x => x.Period == selected)
                .Join(db.Sims.AsNoTracking(), c => c.SimId, s => s.Id, (c, s) => new { c, s }).ToListAsync();
            var scores = model is null ? new Dictionary<int, AnomalyScore>() :
                await db.AnomalyScores.AsNoTracking().Where(x => x.ModelVersionId == model.Id && x.Period == selected)
                    .ToDictionaryAsync(x => x.SimId);
            var reviews = await db.AnomalyReviews.AsNoTracking().Where(x => x.Period == selected).ToDictionaryAsync(x => x.SimId);

            var items = rows.Select(r =>
            {
                var (ruleFlag, ruleReason) = EvaluateRule(r.c.Amount, r.s.MonthlyCost);
                scores.TryGetValue(r.s.Id, out var score);
                reviews.TryGetValue(r.s.Id, out var review);
                return new
                {
                    simId = r.s.Id, r.s.PhoneNumber, r.s.Operator, r.s.PlanType, r.s.CostCentre,
                    period = FormatPeriod(selected), amount = r.c.Amount, contracted = r.s.MonthlyCost,
                    ratio = r.s.MonthlyCost > 0 ? Math.Round(r.c.Amount / r.s.MonthlyCost, 2) : (decimal?)null,
                    r.c.DataUsageMb, r.c.VoiceMinutes, synthetic = r.c.Source == "synthetic",
                    ruleFlag, ruleReason,
                    modelScore = score?.Score, modelFlag = score?.IsFlagged ?? false,
                    modelReasons = score is null ? new List<string>() : JsonSerializer.Deserialize<List<string>>(score.ReasonsJson) ?? [],
                    review = review is null ? null : new { review.Decision, review.Note, review.ReviewedAt, review.FlaggedBy }
                };
            })
            .OrderByDescending(x => x.modelFlag || x.ruleFlag)
            .ThenByDescending(x => x.modelScore ?? double.MinValue)
            .ToList();

            return Results.Ok(new
            {
                periods = periods.Select(FormatPeriod), selectedPeriod = FormatPeriod(selected), model = modelInfo,
                items, performance = await ReviewedPerformanceAsync(db, model)
            });
        }).RequireAuthorization("Session");

        app.MapPost("/api/intelligence/reviews", async (ReviewRequest request, ClaimsPrincipal principal, SimInventoryDbContext db) =>
        {
            if (!TryParsePeriod(request.Period, out var period))
                return Results.BadRequest(new { message = "period must be YYYY-MM." });
            var decision = (request.Decision ?? string.Empty).Trim().ToLowerInvariant();
            if (decision is not (ReviewDecision.Confirmed or ReviewDecision.Dismissed))
                return Results.BadRequest(new { message = "decision must be 'confirmed' or 'dismissed'." });
            var note = (request.Note ?? string.Empty).Trim();
            if (note.Length > 1000) return Results.BadRequest(new { message = "note is limited to 1000 characters." });
            var cost = await db.CostRecords.AsNoTracking().SingleOrDefaultAsync(x => x.SimId == request.SimId && x.Period == period);
            var sim = await db.Sims.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.SimId);
            if (cost is null || sim is null) return Results.NotFound(new { message = "No billed amount exists for that SIM and period." });

            var model = await db.ModelVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Status == ModelStatus.Active)
                ?? await db.ModelVersions.AsNoTracking().Where(x => x.Status == ModelStatus.Candidate).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
            var modelFlag = model is not null && await db.AnomalyScores.AnyAsync(x =>
                x.ModelVersionId == model.Id && x.SimId == sim.Id && x.Period == period && x.IsFlagged);
            var ruleFlag = EvaluateRule(cost.Amount, sim.MonthlyCost).Flagged;
            var flaggedBy = (ruleFlag, modelFlag) switch { (true, true) => "both", (true, false) => "rule", (false, true) => "model", _ => "none" };

            var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var review = await db.AnomalyReviews.SingleOrDefaultAsync(x => x.SimId == sim.Id && x.Period == period);
            var before = review is null ? null : new { review.Decision, review.Note };
            if (review is null)
            {
                review = new AnomalyReview { SimId = sim.Id, Period = period };
                db.AnomalyReviews.Add(review);
            }
            review.Decision = decision; review.Note = note; review.FlaggedBy = flaggedBy;
            review.ModelVersionId = model?.Id; review.ReviewedByUserId = actorId; review.ReviewedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await AuditWriter.WriteAsync(db, principal, "anomaly_reviewed", "sim", sim.Id.ToString(), JsonSerializer.Serialize(new
            {
                period = FormatPeriod(period), decision, flaggedBy, modelVersion = model?.Version, before
            }));
            await transaction.CommitAsync();
            return Results.Ok(new { review.SimId, period = FormatPeriod(period), review.Decision, review.FlaggedBy });
        }).RequireAuthorization("Administrators");

        app.MapGet("/api/ml/models", async (SimInventoryDbContext db) =>
        {
            var models = await db.ModelVersions.AsNoTracking().OrderByDescending(x => x.Id).Take(20).ToListAsync();
            return Results.Ok(models.Select(m => new
            {
                m.Id, m.Version, m.Algorithm, m.Status, m.TrainedAt, m.RegisteredAt, m.ActivatedAt, m.TrainingRows,
                m.LabelledRows, m.LabelSource, m.DatasetSha256, m.ArtifactSha256, m.GitCommit,
                features = JsonSerializer.Deserialize<List<string>>(m.FeaturesJson),
                metrics = JsonDocument.Parse(m.MetricsJson).RootElement
            }));
        }).RequireAuthorization("Session");

        app.MapPost("/api/ml/models/{id:int}/activate", async (int id, ClaimsPrincipal principal, SimInventoryDbContext db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var model = await db.ModelVersions.SingleOrDefaultAsync(x => x.Id == id);
            if (model is null) return Results.NotFound();
            if (model.Status == ModelStatus.Active) return Results.Conflict(new { message = "This model is already active." });
            var current = await db.ModelVersions.SingleOrDefaultAsync(x => x.Status == ModelStatus.Active);
            if (current is not null)
            {
                current.Status = ModelStatus.Retired;
                await db.SaveChangesAsync(); // release the single-active index before activating the new one
            }
            model.Status = ModelStatus.Active;
            model.ActivatedAt = DateTimeOffset.UtcNow;
            model.ActivatedByUserId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await db.SaveChangesAsync();
            await AuditWriter.WriteAsync(db, principal, "ml_model_activated", "model_version", model.Id.ToString(),
                JsonSerializer.Serialize(new { model.Version, previousActive = current?.Version, rollback = current is not null && current.Id > model.Id }));
            await transaction.CommitAsync();
            return Results.Ok(new { model.Id, model.Version, model.Status, previousActive = current?.Version });
        }).RequireAuthorization("Administrators");
    }

    /// <summary>
    /// Live precision and recall of the rule and of the shown model, measured only on SIM-months a human reviewed.
    /// Returns null counts until there are reviews; it never uses synthetic scenario labels.
    /// </summary>
    private static async Task<object> ReviewedPerformanceAsync(SimInventoryDbContext db, ModelVersion? model)
    {
        var reviewed = await db.AnomalyReviews.AsNoTracking()
            .Join(db.CostRecords.AsNoTracking(), r => new { r.SimId, r.Period }, c => new { c.SimId, c.Period }, (r, c) => new { r, c })
            .Join(db.Sims.AsNoTracking(), rc => rc.r.SimId, s => s.Id, (rc, s) => new { rc.r, rc.c, s })
            .ToListAsync();
        var flaggedByModel = model is null ? new HashSet<(int, DateOnly)>() :
            (await db.AnomalyScores.AsNoTracking().Where(x => x.ModelVersionId == model.Id && x.IsFlagged)
                .Select(x => new { x.SimId, x.Period }).ToListAsync()).Select(x => (x.SimId, x.Period)).ToHashSet();

        object Summarise(Func<int, DateOnly, decimal, decimal, bool> flagged)
        {
            int tp = 0, fp = 0, fn = 0, tn = 0;
            foreach (var x in reviewed)
            {
                var f = flagged(x.s.Id, x.r.Period, x.c.Amount, x.s.MonthlyCost);
                var confirmed = x.r.Decision == ReviewDecision.Confirmed;
                if (f && confirmed) tp++; else if (f) fp++; else if (confirmed) fn++; else tn++;
            }
            return new
            {
                confirmedFlags = tp, dismissedFlags = fp, missedConfirmed = fn, correctlyQuiet = tn,
                precision = tp + fp == 0 ? (double?)null : Math.Round((double)tp / (tp + fp), 3),
                recall = tp + fn == 0 ? (double?)null : Math.Round((double)tp / (tp + fn), 3)
            };
        }

        return new
        {
            reviewedSimMonths = reviewed.Count,
            confirmed = reviewed.Count(x => x.r.Decision == ReviewDecision.Confirmed),
            rule = Summarise((_, _, amount, contracted) => EvaluateRule(amount, contracted).Flagged),
            model = model is null ? null : Summarise((simId, p, _, _) => flaggedByModel.Contains((simId, p)))
        };
    }
}
