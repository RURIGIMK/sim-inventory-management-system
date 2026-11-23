namespace SimInventory.Api.Models;

/// <summary>
/// One billed amount for one SIM for one calendar month, taken from an operator invoice.
/// <see cref="Sim.MonthlyCost"/> is the contracted (expected) monthly fee; this record is what was actually billed.
/// </summary>
public sealed class SimCostRecord
{
    public long Id { get; set; }
    public int SimId { get; set; }

    /// <summary>First day of the billed month.</summary>
    public DateOnly Period { get; set; }
    public decimal Amount { get; set; }
    public int? DataUsageMb { get; set; }
    public int? VoiceMinutes { get; set; }

    /// <summary>operator_invoice or synthetic.</summary>
    public string Source { get; set; } = "operator_invoice";

    /// <summary>
    /// Ground-truth label for generated demonstration data only. Used to evaluate models,
    /// never as a model feature, and always null for real invoice data.
    /// </summary>
    public string? SyntheticScenario { get; set; }
    public Guid? ImportBatchId { get; set; }
    public Guid? ImportedByUserId { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ModelStatus
{
    public const string Candidate = "candidate";
    public const string Active = "active";
    public const string Retired = "retired";
}

/// <summary>A trained anomaly model registered by the scheduled pipeline. Only an administrator can activate one.</summary>
public sealed class ModelVersion
{
    public int Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public string Status { get; set; } = ModelStatus.Candidate;
    public DateTimeOffset TrainedAt { get; set; }
    public int TrainingRows { get; set; }
    public int LabelledRows { get; set; }
    public string LabelSource { get; set; } = string.Empty;
    public string DatasetSha256 { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "[]";
    public string MetricsJson { get; set; } = "{}";
    public byte[] Artifact { get; set; } = [];
    public string ArtifactSha256 { get; set; } = string.Empty;
    public string? GitCommit { get; set; }
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ActivatedAt { get; set; }
    public Guid? ActivatedByUserId { get; set; }
}

public sealed class AnomalyScore
{
    public long Id { get; set; }
    public int ModelVersionId { get; set; }
    public int SimId { get; set; }
    public DateOnly Period { get; set; }
    public double Score { get; set; }
    public bool IsFlagged { get; set; }
    public string ReasonsJson { get; set; } = "[]";
    public DateTimeOffset ScoredAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ReviewDecision
{
    public const string Confirmed = "confirmed";
    public const string Dismissed = "dismissed";
}

/// <summary>A human decision about one SIM-month. Reviews are the only real labels the model is evaluated against.</summary>
public sealed class AnomalyReview
{
    public long Id { get; set; }
    public int SimId { get; set; }
    public DateOnly Period { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string FlaggedBy { get; set; } = "none";
    public int? ModelVersionId { get; set; }
    public Guid ReviewedByUserId { get; set; }
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class EmployeeSyncRun
{
    public long Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public int Received { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Deactivated { get; set; }
    public int Unchanged { get; set; }
    public int Rejected { get; set; }
    public string Outcome { get; set; } = "running";
    public string ErrorsJson { get; set; } = "[]";
    public string TriggeredBy { get; set; } = "system";
}

public sealed record ReviewRequest(int SimId, string Period, string Decision, string? Note);

public sealed record RegisterModelRequest(
    string Version, string Algorithm, DateTimeOffset TrainedAt, int TrainingRows, int LabelledRows,
    string LabelSource, string DatasetSha256, List<string> Features,
    System.Text.Json.JsonElement Metrics, string ArtifactBase64, string? GitCommit);

public sealed record ScoreItem(int SimId, string Period, double Score, bool IsFlagged, List<string>? Reasons);
public sealed record PublishScoresRequest(int ModelVersionId, List<ScoreItem> Scores);
