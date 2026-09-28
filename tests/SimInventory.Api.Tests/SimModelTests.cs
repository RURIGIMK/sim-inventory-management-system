using SimInventory.Api.Models;

namespace SimInventory.Api.Tests;

public class SimModelTests
{
    [Fact]
    public void NewSimDefaultsToAvailable()
    {
        var sim = new Sim();

        Assert.Equal(SimStatus.Available, sim.Status);
    }

    [Fact]
    public void SimStoresCoreInventoryFields()
    {
        var sim = new Sim
        {
            Operator = "Safaricom",
            PhoneNumber = "254700000001",
            Iccid = "893100000000000001",
            PlanType = "Corporate Data",
            MonthlyCost = 1500m,
            CostCentre = "CC-1001"
        };

        Assert.Equal("Safaricom", sim.Operator);
        Assert.Equal("893100000000000001", sim.Iccid);
        Assert.Equal(1500m, sim.MonthlyCost);
        Assert.Equal("CC-1001", sim.CostCentre);
    }
}
