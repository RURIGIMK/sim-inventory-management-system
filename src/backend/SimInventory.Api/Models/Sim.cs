namespace SimInventory.Api.Models;

public enum SimStatus
{
    Available,
    Allocated,
    Suspended,
    Deactivated
}

public class Sim
{
    public int Id { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Iccid { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public decimal MonthlyCost { get; set; }
    public DateOnly? ContractStart { get; set; }
    public DateOnly? ContractEnd { get; set; }
    public SimStatus Status { get; set; } = SimStatus.Available;
    public string CostCentre { get; set; } = string.Empty;
}
