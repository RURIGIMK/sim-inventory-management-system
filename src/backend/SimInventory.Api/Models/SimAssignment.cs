namespace SimInventory.Api.Models;

public sealed class SimAssignment
{
    public int Id { get; set; }
    public int SimId { get; set; }
    public Sim Sim { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReturnedAt { get; set; }
    public Guid? ReturnedByUserId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsActive => ReturnedAt is null;
}

public sealed record AllocateSimRequest(int SimId, int EmployeeId, string? Notes);
public sealed record ReassignSimRequest(int EmployeeId, string? Notes);
