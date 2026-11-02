using System.Text.Json;
using SimInventory.Api.Models;
using SimInventory.Api.Security;
using Xunit;

namespace SimInventory.Api.Tests;

public class AuditChainRecoveryTests
{
    [Fact]
    public void DetectsHistoricalForkAndVerifiesExplicitRecoveryCheckpoint()
    {
        var events = CreateForkedHistory();
        Assert.True(AuditChainRecovery.TryBuildEvidence(events, out var evidence, out var error), error);
        Assert.NotNull(evidence);
        Assert.Equal(10, evidence!.FirstForkEventId);
        Assert.Equal(11, evidence.SecondForkEventId);
        Assert.Equal(14, evidence.SelectedBranchHeadEventId);
        Assert.Equal(events[9].Hash, evidence.OrphanedBranchHash);

        var details = JsonSerializer.Serialize(evidence);
        var checkpoint = CreateEvent(15, events[^1].Hash, "admin@example.test",
            AuditChainRecovery.CheckpointAction, "audit_chain", "historical-fork", details);
        var recovered = events.Append(checkpoint).ToList();

        Assert.True(AuditChainRecovery.Verify(recovered, out var verifiedEvidence,
            out var checkpointId, out var invalidEventId));
        Assert.Equal(15, checkpointId);
        Assert.Null(invalidEventId);
        Assert.Equal(evidence, verifiedEvidence);
    }

    [Fact]
    public void RejectsChangesToEventsAfterRecoveryCheckpoint()
    {
        var events = CreateForkedHistory();
        Assert.True(AuditChainRecovery.TryBuildEvidence(events, out var evidence, out _));
        var details = JsonSerializer.Serialize(evidence);
        var checkpoint = CreateEvent(15, events[^1].Hash, "admin@example.test",
            AuditChainRecovery.CheckpointAction, "audit_chain", "historical-fork", details);
        var later = CreateEvent(16, checkpoint.Hash, "admin@example.test",
            "login_successful", "user", "admin", "{}");
        later.Details = "{\"changed\":true}";

        Assert.False(AuditChainRecovery.Verify(events.Append(checkpoint).Append(later).ToList(),
            out _, out _, out var invalidEventId));
        Assert.Equal(16, invalidEventId);
    }

    [Fact]
    public void RejectsForkWithoutRecoveryCheckpoint()
    {
        Assert.False(AuditChainRecovery.Verify(CreateForkedHistory(), out _, out _, out var invalidEventId));
        Assert.Equal(11, invalidEventId);
    }

    private static List<AuditEvent> CreateForkedHistory()
    {
        var events = new List<AuditEvent>();
        var previous = "GENESIS";
        for (var id = 1; id <= 9; id++)
        {
            var item = CreateEvent(id, previous, "admin@example.test",
                id == 9 ? "sim_allocated" : "login_successful", "user", id.ToString(), "{}");
            events.Add(item);
            previous = item.Hash;
        }

        var firstSibling = CreateEvent(10, previous, "admin@example.test",
            "login_successful", "user", "admin", "{\"branch\":10}");
        var secondSibling = CreateEvent(11, previous, "admin@example.test",
            "login_successful", "user", "admin", "{\"branch\":11}");
        events.Add(firstSibling);
        events.Add(secondSibling);

        previous = secondSibling.Hash;
        for (var id = 12; id <= 14; id++)
        {
            var item = CreateEvent(id, previous, "admin@example.test",
                "login_successful", "user", "admin", "{}");
            events.Add(item);
            previous = item.Hash;
        }
        return events;
    }

    private static AuditEvent CreateEvent(long id, string previous, string actor,
        string action, string entityType, string entityId, string details)
    {
        var created = new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero).AddSeconds(id);
        return new AuditEvent
        {
            Id = id,
            CreatedAt = created,
            ActorEmail = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            PreviousHash = previous,
            Hash = AuditHash.ComputeHash(previous, created, actor, action, entityType, entityId, details)
        };
    }
}
