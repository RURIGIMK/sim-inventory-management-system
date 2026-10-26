using SimInventory.Api.Security;
using Xunit;

namespace SimInventory.Api.Tests;

public class AuditHashTests
{
    [Fact]
    public void HashIsStableAcrossPostgresMicrosecondTimestampPrecision()
    {
        var original = new DateTimeOffset(2026, 10, 26, 12, 34, 56, TimeSpan.Zero).AddTicks(1234567);
        var persistedPrecision = AuditHash.NormalizeTimestamp(original);

        var beforePersistence = AuditHash.ComputeHash("GENESIS", original, "admin@example.test",
            "sim_registered", "sim", "12", "{\"operator\":\"Example\"}");
        var afterPersistence = AuditHash.ComputeHash("GENESIS", persistedPrecision, "admin@example.test",
            "sim_registered", "sim", "12", "{\"operator\":\"Example\"}");

        Assert.Equal(persistedPrecision, AuditHash.NormalizeTimestamp(original));
        Assert.Equal(beforePersistence, afterPersistence);
    }

    [Fact]
    public void HashChangesWhenAuditedDetailsChange()
    {
        var created = new DateTimeOffset(2026, 10, 26, 12, 34, 56, TimeSpan.Zero);
        var first = AuditHash.ComputeHash("GENESIS", created, "admin@example.test",
            "sim_registered", "sim", "12", "{\"operator\":\"Example\"}");
        var changed = AuditHash.ComputeHash("GENESIS", created, "admin@example.test",
            "sim_registered", "sim", "12", "{\"operator\":\"Changed\"}");

        Assert.NotEqual(first, changed);
    }
}
