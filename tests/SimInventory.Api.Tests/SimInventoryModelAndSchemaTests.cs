using Microsoft.EntityFrameworkCore;
using SimInventory.Api.Data;
using SimInventory.Api.Models;
using Xunit;

namespace SimInventory.Api.Tests;

public class SimInventoryModelAndSchemaTests
{
    [Fact]
    public void AssignmentIsActiveUntilItHasBeenReturned()
    {
        var assignment = new SimAssignment();

        Assert.True(assignment.IsActive);

        assignment.ReturnedAt = DateTimeOffset.UtcNow;

        Assert.False(assignment.IsActive);
    }

    [Fact]
    public void SimStatusSupportsTheExpectedLifecycleStates()
    {
        Assert.Contains(SimStatus.Available, Enum.GetValues<SimStatus>());
        Assert.Contains(SimStatus.Allocated, Enum.GetValues<SimStatus>());
        Assert.Contains(SimStatus.Suspended, Enum.GetValues<SimStatus>());
        Assert.Contains(SimStatus.Deactivated, Enum.GetValues<SimStatus>());
    }

    [Fact]
    public void SimIdentityFieldsHaveUniqueDatabaseIndexes()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(Sim));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(Sim.Iccid) }));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(Sim.PhoneNumber) }));
    }

    [Fact]
    public void OnlyOneUnreturnedAssignmentCanExistForASim()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(SimAssignment));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(SimAssignment.SimId) }) &&
            index.GetFilter() == "\"ReturnedAt\" IS NULL");
    }

    [Fact]
    public void AssignmentForeignKeysRestrictDeletingReferencedRecords()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(SimAssignment));

        Assert.NotNull(entity);
        Assert.All(entity!.GetForeignKeys(), foreignKey =>
            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
    }

    [Fact]
    public void MonthlyCostUsesTwoDecimalPlaces()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(Sim));

        Assert.NotNull(entity);
        Assert.Equal(12, entity!.FindProperty(nameof(Sim.MonthlyCost))!.GetPrecision());
        Assert.Equal(2, entity.FindProperty(nameof(Sim.MonthlyCost))!.GetScale());
    }

    private static SimInventoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SimInventoryDbContext>()
            .UseNpgsql("Host=localhost;Database=sim_inventory_test;Username=test;Password=test")
            .Options;
        return new SimInventoryDbContext(options);
    }
}
