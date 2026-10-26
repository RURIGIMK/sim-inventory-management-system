using SimInventory.Api.Intelligence;
using SimInventory.Api.Models;
using Xunit;

namespace SimInventory.Api.Tests;

public class SimAssetIntelligenceTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    [Fact]
    public void HighCostAlertExplainsThePeerComparison()
    {
        var sims = new[]
        {
            MakeSim(1, 1000m), MakeSim(2, 1000m), MakeSim(3, 1000m),
            MakeSim(4, 1000m), MakeSim(5, 4000m)
        };

        var result = SimAssetIntelligence.Evaluate(sims, Array.Empty<SimAssignment>(),
            Array.Empty<Employee>(), Today);

        var alert = Assert.Single(result.Alerts.Where(x => x.Code == "UNUSUAL_MONTHLY_COST"));
        Assert.Equal(5, alert.EntityId);
        Assert.Equal(4000m, alert.ObservedMonthlyCost);
        Assert.Equal(1000m, alert.PeerMedianMonthlyCost);
        Assert.Contains("threshold", alert.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invoice", alert.RecommendedAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CostComparisonIsSkippedWhenThereAreTooFewPeers()
    {
        var sims = new[] { MakeSim(1, 1000m), MakeSim(2, 5000m), MakeSim(3, 1200m) };

        var result = SimAssetIntelligence.Evaluate(sims, Array.Empty<SimAssignment>(),
            Array.Empty<Employee>(), Today);

        Assert.Empty(result.Alerts.Where(x => x.Code == "UNUSUAL_MONTHLY_COST"));
        Assert.Equal(3, result.CostComparisonsSkipped);
    }

    [Fact]
    public void ContractEndingWithinThirtyDaysRaisesReviewAlert()
    {
        var sim = MakeSim(1, 1000m);
        sim.ContractEnd = Today.AddDays(12);

        var result = SimAssetIntelligence.Evaluate(new[] { sim }, Array.Empty<SimAssignment>(),
            Array.Empty<Employee>(), Today);

        var alert = Assert.Single(result.Alerts.Where(x => x.Code == "CONTRACT_EXPIRING"));
        Assert.Equal("medium", alert.Severity);
        Assert.Equal(Today.AddDays(12), alert.ContractEnd);
    }

    [Fact]
    public void AllocatedSimWithoutActiveAssignmentRaisesIntegrityAlert()
    {
        var sim = MakeSim(1, 1000m);
        sim.Status = SimStatus.Allocated;

        var result = SimAssetIntelligence.Evaluate(new[] { sim }, Array.Empty<SimAssignment>(),
            Array.Empty<Employee>(), Today);

        Assert.Contains(result.Alerts, x => x.Code == "ALLOCATED_WITHOUT_ASSIGNMENT");
    }

    [Fact]
    public void ActiveAssignmentToInactiveEmployeeRaisesIntegrityAlert()
    {
        var sim = MakeSim(1, 1000m);
        sim.Status = SimStatus.Allocated;
        var employee = new Employee
        {
            Id = 10,
            EmployeeNumber = "EMP-010",
            FullName = "Synthetic Employee",
            Department = "Operations",
            CostCentre = "OPS-001",
            Status = EmploymentStatus.Terminated
        };
        var assignment = new SimAssignment { Id = 20, SimId = sim.Id, EmployeeId = employee.Id };

        var result = SimAssetIntelligence.Evaluate(new[] { sim }, new[] { assignment },
            new[] { employee }, Today);

        Assert.Contains(result.Alerts, x => x.Code == "INACTIVE_EMPLOYEE_ASSIGNMENT");
    }

    private static Sim MakeSim(int id, decimal cost) => new()
    {
        Id = id,
        Operator = "Example Operator",
        PhoneNumber = $"254700000{id:000}",
        Iccid = $"893100000000000{id:000}",
        MonthlyCost = cost,
        Status = SimStatus.Available
    };
}
