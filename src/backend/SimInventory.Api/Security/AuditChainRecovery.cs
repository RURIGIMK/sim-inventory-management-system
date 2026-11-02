using System.Security.Claims;
using System.Text.Json;
using SimInventory.Api.Models;

namespace SimInventory.Api.Security;

public sealed record AuditRecoveryEvidence(
    long FirstForkEventId,
    long SecondForkEventId,
    string ForkParentHash,
    long SelectedBranchHeadEventId,
    string SelectedBranchHeadHash,
    string OrphanedBranchHash);

public static class AuditChainRecovery
{
    public const string CheckpointAction = "audit_chain_recovery_checkpoint";

    public static bool TryBuildEvidence(IReadOnlyList<AuditEvent> events,
        out AuditRecoveryEvidence? evidence, out string error)
    {
        evidence = null;
        error = string.Empty;
        if (events.Count < 3)
        {
            error = "There are not enough events to identify a recoverable fork.";
            return false;
        }

        var previous = "GENESIS";
        var forkIndex = -1;
        for (var i = 0; i < events.Count; i++)
        {
            var entry = events[i];
            var expected = AuditHash.ComputeHash(previous, entry.CreatedAt, entry.ActorEmail,
                entry.Action, entry.EntityType, entry.EntityId, entry.Details);
            if (entry.PreviousHash != previous || entry.Hash != expected)
            {
                forkIndex = i;
                break;
            }
            previous = entry.Hash;
        }

        if (forkIndex < 1 || forkIndex >= events.Count)
        {
            error = "No single recoverable fork was found.";
            return false;
        }

        var first = events[forkIndex - 1];
        var second = events[forkIndex];
        var parentHash = first.PreviousHash;
        if (second.PreviousHash != parentHash ||
            !HasValidHash(first, parentHash) || !HasValidHash(second, parentHash))
        {
            error = "The first invalid event is not a pair of independently valid sibling events.";
            return false;
        }

        // The first sibling must itself be linked to the valid prefix.
        var prefixPrevious = forkIndex == 1 ? "GENESIS" : events[forkIndex - 2].Hash;
        if (first.PreviousHash != prefixPrevious)
        {
            error = "The chain before the fork is not continuous.";
            return false;
        }

        AuditEvent selected;
        AuditEvent orphaned;
        if (forkIndex + 1 < events.Count)
        {
            var next = events[forkIndex + 1];
            var firstContinues = next.PreviousHash == first.Hash;
            var secondContinues = next.PreviousHash == second.Hash;
            if (firstContinues == secondContinues)
            {
                error = "The continuing branch cannot be identified unambiguously.";
                return false;
            }
            selected = firstContinues ? first : second;
            orphaned = firstContinues ? second : first;
            var branchPrevious = selected.Hash;
            for (var i = forkIndex + 1; i < events.Count; i++)
            {
                var item = events[i];
                if (item.PreviousHash != branchPrevious || !HasValidHash(item, branchPrevious))
                {
                    error = $"The selected branch is broken at event {item.Id}.";
                    return false;
                }
                branchPrevious = item.Hash;
            }
        }
        else
        {
            error = "A continuing branch is required before recovery.";
            return false;
        }

        var head = events[^1];
        evidence = new AuditRecoveryEvidence(first.Id, second.Id, parentHash,
            head.Id, head.Hash, orphaned.Hash);
        return true;
    }

    public static bool Verify(IReadOnlyList<AuditEvent> events,
        out AuditRecoveryEvidence? evidence, out long? checkpointId, out long? invalidEventId)
    {
        evidence = null;
        checkpointId = null;
        invalidEventId = null;
        var checkpoints = events.Select((entry, index) => (entry, index))
            .Where(x => x.entry.Action == CheckpointAction).ToList();

        if (checkpoints.Count != 1)
        {
            var previous = "GENESIS";
            foreach (var entry in events)
            {
                if (entry.PreviousHash != previous || !HasValidHash(entry, previous))
                {
                    invalidEventId = entry.Id;
                    return false;
                }
                previous = entry.Hash;
            }
            return checkpoints.Count == 0;
        }

        var (checkpoint, checkpointIndex) = checkpoints[0];
        if (checkpointIndex < 3 || !TryBuildEvidence(events.Take(checkpointIndex).ToList(),
            out evidence, out _))
        {
            invalidEventId = checkpoint.Id;
            return false;
        }

        var expectedDetails = JsonSerializer.Serialize(evidence);
        if (checkpoint.Details != expectedDetails ||
            checkpoint.PreviousHash != evidence!.SelectedBranchHeadHash ||
            !HasValidHash(checkpoint, evidence.SelectedBranchHeadHash))
        {
            invalidEventId = checkpoint.Id;
            return false;
        }

        var previousHash = checkpoint.Hash;
        for (var i = checkpointIndex + 1; i < events.Count; i++)
        {
            var entry = events[i];
            if (entry.PreviousHash != previousHash || !HasValidHash(entry, previousHash))
            {
                invalidEventId = entry.Id;
                return false;
            }
            previousHash = entry.Hash;
        }

        checkpointId = checkpoint.Id;
        return true;
    }

    private static bool HasValidHash(AuditEvent entry, string previousHash) =>
        entry.Hash == AuditHash.ComputeHash(previousHash, entry.CreatedAt, entry.ActorEmail,
            entry.Action, entry.EntityType, entry.EntityId, entry.Details);
}
