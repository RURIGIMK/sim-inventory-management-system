namespace SimInventory.Api.Models;

public enum EmploymentStatus
{
    Active,
    Inactive,
    Terminated
}

public sealed class Employee
{
    public int Id { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Department { get; set; } = string.Empty;
    public string CostCentre { get; set; } = string.Empty;
    public EmploymentStatus Status { get; set; } = EmploymentStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>When the record was last confirmed by the HR source of truth.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Where the record came from: seed, csv_import, hr_api or hr_webhook.</summary>
    public string Source { get; set; } = "seed";
}


