using SimInventory.Api.Models;

namespace SimInventory.Api.Intelligence;

public sealed record SimAssetAlert(
    string Code,
    string Severity,
    string Title,
    string EntityType,
    int EntityId,
    string EntityLabel,
    string Explanation,
    decimal? ObservedMonthlyCost,
    decimal? PeerMedianMonthlyCost,
    DateOnly? ContractEnd,
    string RecommendedAction);

public sealed record SimAssetIntelligenceResult(
    DateOnly EvaluatedOn,
    int SimsEvaluated,
    int AlertsRaised,
    int CostComparisonsSkipped,
    IReadOnlyList<SimAssetAlert> Alerts);

/// <summary>
/// Explainable, deterministic inventory checks. These are review flags, not predictions.
/// </summary>
public static class SimAssetIntelligence
{
    private const int MinimumCostPeers = 4;

    public static SimAssetIntelligenceResult Evaluate(
        IReadOnlyCollection<Sim> sims,
        IReadOnlyCollection<SimAssignment> assignments,
        IReadOnlyCollection<Employee> employees,
        DateOnly today)
    {
        var alerts = new List<SimAssetAlert>();
        var activeAssignments = assignments.Where(x => x.ReturnedAt is null).ToList();
        var assignmentGroups = activeAssignments.GroupBy(x => x.SimId).ToDictionary(x => x.Key, x => x.ToList());
        var employeeById = employees.ToDictionary(x => x.Id);

        foreach (var sim in sims)
        {
            if (sim.ContractEnd.HasValue && sim.Status != SimStatus.Deactivated)
            {
                var daysRemaining = sim.ContractEnd.Value.DayNumber - today.DayNumber;
                if (daysRemaining < 0)
                {
                    alerts.Add(new SimAssetAlert(
                        "CONTRACT_OVERDUE", "high", "Contract end date has passed", "sim", sim.Id,
                        sim.PhoneNumber, $"Contract ended {Math.Abs(daysRemaining)} day(s) ago.",
                        null, null, sim.ContractEnd,
                        "Confirm the contract status with the operator and update the inventory record."));
                }
                else if (daysRemaining <= 30)
                {
                    alerts.Add(new SimAssetAlert(
                        "CONTRACT_EXPIRING", daysRemaining <= 7 ? "high" : "medium",
                        "Contract ending soon", "sim", sim.Id, sim.PhoneNumber,
                        $"Contract ends in {daysRemaining} day(s).", null, null, sim.ContractEnd,
                        "Review renewal, cancellation or replacement before the contract end date."));
                }
            }

            if (sim.Status == SimStatus.Allocated && !assignmentGroups.ContainsKey(sim.Id))
            {
                alerts.Add(new SimAssetAlert(
                    "ALLOCATED_WITHOUT_ASSIGNMENT", "high", "Allocated SIM has no active assignment",
                    "sim", sim.Id, sim.PhoneNumber,
                    "The SIM status is Allocated, but no unreturned assignment record exists.",
                    null, null, sim.ContractEnd,
                    "Check the assignment history and correct the SIM status or restore the missing assignment record."));
            }

            if (assignmentGroups.TryGetValue(sim.Id, out var simAssignments))
            {
                if (simAssignments.Count > 1)
                {
                    alerts.Add(new SimAssetAlert(
                        "MULTIPLE_ACTIVE_ASSIGNMENTS", "high", "SIM has multiple active assignments",
                        "sim", sim.Id, sim.PhoneNumber,
                        $"Found {simAssignments.Count} unreturned assignments for one SIM.",
                        null, null, sim.ContractEnd,
                        "Review assignment history and resolve the duplicate active records."));
                }

                if (sim.Status != SimStatus.Allocated)
                {
                    alerts.Add(new SimAssetAlert(
                        "ASSIGNMENT_STATUS_MISMATCH", "high", "Assignment and SIM status disagree",
                        "sim", sim.Id, sim.PhoneNumber,
                        $"An active assignment exists while the SIM status is {sim.Status}.",
                        null, null, sim.ContractEnd,
                        "Verify the current custodian and reconcile the SIM status with its assignment history."));
                }

                foreach (var assignment in simAssignments)
                {
                    if (employeeById.TryGetValue(assignment.EmployeeId, out var employee) &&
                        employee.Status != EmploymentStatus.Active)
                    {
                        alerts.Add(new SimAssetAlert(
                            "INACTIVE_EMPLOYEE_ASSIGNMENT", "high", "SIM assigned to an inactive employee",
                            "sim", sim.Id, sim.PhoneNumber,
                            $"Active assignment points to employee {employee.EmployeeNumber}, whose status is {employee.Status}.",
                            null, null, sim.ContractEnd,
                            "Confirm custody and return or reassign the SIM after verifying the employee record."));
                    }
                }
            }
        }

        var costComparisonsSkipped = 0;
        foreach (var sim in sims.Where(x => x.MonthlyCost >= 0 && x.Status != SimStatus.Deactivated))
        {
            var peers = sims
                .Where(other => other.Id != sim.Id &&
                    other.MonthlyCost >= 0 &&
                    other.Status != SimStatus.Deactivated &&
                    string.Equals(other.Operator.Trim(), sim.Operator.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(other => other.MonthlyCost)
                .OrderBy(value => value)
                .ToArray();

            if (peers.Length < MinimumCostPeers)
            {
                costComparisonsSkipped++;
                continue;
            }

            var median = Median(peers);
            if (median <= 0m)
            {
                costComparisonsSkipped++;
                continue;
            }

            var deviations = peers.Select(value => Math.Abs(value - median)).OrderBy(value => value).ToArray();
            var mad = Median(deviations);
            var threshold = median + Math.Max(3m * 1.4826m * mad, Math.Abs(median) * 0.5m);

            if (sim.MonthlyCost > threshold)
            {
                alerts.Add(new SimAssetAlert(
                    "UNUSUAL_MONTHLY_COST", "medium", "Monthly cost is unusually high",
                    "sim", sim.Id, sim.PhoneNumber,
                    $"Monthly cost {sim.MonthlyCost:0.00} exceeds the peer threshold {threshold:0.00} for operator {sim.Operator}.",
                    sim.MonthlyCost, median, sim.ContractEnd,
                    "Check the plan, billing period and invoice before deciding whether the cost is valid."));
            }
        }

        var ordered = alerts
            .OrderByDescending(x => x.Severity == "high")
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.EntityId)
            .ToArray();

        return new SimAssetIntelligenceResult(today, sims.Count, ordered.Length, costComparisonsSkipped, ordered);
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0) return 0m;
        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2m;
    }
}
