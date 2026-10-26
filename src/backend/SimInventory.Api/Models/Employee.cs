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
}

public sealed record CreateEmployeeRequest(string EmployeeNumber, string FullName, string? Email, string Department, string CostCentre);
public sealed record UpdateEmployeeStatusRequest(EmploymentStatus Status);
