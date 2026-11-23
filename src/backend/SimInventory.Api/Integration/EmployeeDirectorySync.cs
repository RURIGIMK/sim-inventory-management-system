using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimInventory.Api.Data;
using SimInventory.Api.Models;
using SimInventory.Api.Security;

namespace SimInventory.Api.Integration;

/// <summary>One employee as published by the organisation's HR system of record.</summary>
public sealed record HrEmployeeRecord(
    string? EmployeeNumber, string? FullName, string? Email, string? Department, string? CostCentre, string? Status);

/// <summary>
/// The payload contract shared by every HR adapter (CSV upload, scheduled pull, signed webhook).
/// mode "snapshot" means the list is the complete active workforce; "delta" means only changed people.
/// </summary>
public sealed record HrDirectoryPayload(string? Mode, List<HrEmployeeRecord>? Employees, bool? AllowLargeDeactivation = null);

public sealed record SyncOutcome(EmployeeSyncRun Run, IReadOnlyList<object> HoldersNeedingRecovery);

/// <summary>
/// Applies HR directory data to the read-only employee directory. HR is the source of truth:
/// the application never edits people by hand. Every run is recorded, audited and transactional.
/// </summary>
public static class EmployeeDirectorySync
{
    /// <summary>Safety stop: a snapshot that would deactivate more than this share of active staff is refused.</summary>
    public const double MaxSnapshotDeactivationShare = 0.30;

    public static async Task<SyncOutcome> ApplyAsync(SimInventoryDbContext db, HrDirectoryPayload payload, string source,
        ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        var mode = (payload.Mode ?? "delta").Trim().ToLowerInvariant();
        if (mode is not ("snapshot" or "delta"))
            throw new ArgumentException("mode must be 'snapshot' or 'delta'.");
        var incoming = payload.Employees ?? [];

        var run = new EmployeeSyncRun
        {
            Source = source, Mode = mode, Received = incoming.Count,
            TriggeredBy = actor.FindFirstValue(ClaimTypes.Email) ?? source
        };
        var errors = new List<string>();
        var valid = new Dictionary<string, (HrEmployeeRecord Record, EmploymentStatus Status)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < incoming.Count; i++)
        {
            var r = incoming[i];
            var row = $"Record {i + 1}";
            var number = r.EmployeeNumber?.Trim();
            if (string.IsNullOrWhiteSpace(number)) { errors.Add($"{row}: employee number is required."); continue; }
            if (string.IsNullOrWhiteSpace(r.FullName)) { errors.Add($"{row} ({number}): full name is required."); continue; }
            if (string.IsNullOrWhiteSpace(r.Department)) { errors.Add($"{row} ({number}): department is required."); continue; }
            if (!TryParseStatus(r.Status, out var status)) { errors.Add($"{row} ({number}): unknown status '{r.Status}'."); continue; }
            if (!string.IsNullOrWhiteSpace(r.Email) && !r.Email.Contains('@')) { errors.Add($"{row} ({number}): invalid email."); continue; }
            if (number.Length > 80 || r.FullName.Length > 200) { errors.Add($"{row} ({number}): value too long."); continue; }
            if (!valid.TryAdd(number, (r, status))) errors.Add($"{row} ({number}): duplicate employee number in this payload.");
        }
        run.Rejected = errors.Count;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var employees = await db.Employees.ToListAsync(cancellationToken);
        var byNumber = employees.ToDictionary(x => x.EmployeeNumber, StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var statusChanges = new List<(Employee Employee, EmploymentStatus From, EmploymentStatus To, string Reason)>();

        foreach (var (number, (record, status)) in valid)
        {
            if (!byNumber.TryGetValue(number, out var employee))
            {
                employee = new Employee
                {
                    EmployeeNumber = number, FullName = record.FullName!.Trim(), Email = Clean(record.Email),
                    Department = record.Department!.Trim(), CostCentre = Clean(record.CostCentre) ?? string.Empty,
                    Status = status, Source = source, LastSyncedAt = now
                };
                db.Employees.Add(employee);
                run.Created++;
                continue;
            }

            var changed = false;
            void Set<T>(T current, T next, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(current, next)) { apply(next); changed = true; } }
            Set(employee.FullName, record.FullName!.Trim(), v => employee.FullName = v);
            Set(employee.Email, Clean(record.Email), v => employee.Email = v);
            Set(employee.Department, record.Department!.Trim(), v => employee.Department = v);
            Set(employee.CostCentre, Clean(record.CostCentre) ?? string.Empty, v => employee.CostCentre = v);
            if (employee.Status != status)
            {
                statusChanges.Add((employee, employee.Status, status, "hr_status"));
                employee.Status = status;
                changed = true;
            }
            employee.LastSyncedAt = now;
            employee.Source = source;
            if (changed) { employee.UpdatedAt = now; run.Updated++; } else run.Unchanged++;
        }

        if (mode == "snapshot")
        {
            var missing = employees.Where(x => x.Status == EmploymentStatus.Active && !valid.ContainsKey(x.EmployeeNumber)).ToList();
            var activeCount = employees.Count(x => x.Status == EmploymentStatus.Active);
            if (activeCount > 0 && missing.Count > activeCount * MaxSnapshotDeactivationShare && payload.AllowLargeDeactivation != true)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Refused: this snapshot would deactivate {missing.Count} of {activeCount} active employees. " +
                    "A truncated HR export is the most likely cause. Re-send the full export, or set allowLargeDeactivation after verifying it.");
            }
            foreach (var employee in missing)
            {
                statusChanges.Add((employee, employee.Status, EmploymentStatus.Inactive, "missing_from_hr_snapshot"));
                employee.Status = EmploymentStatus.Inactive;
                employee.UpdatedAt = now;
                run.Deactivated++;
            }
        }
        run.Deactivated += statusChanges.Count(x => x.Reason == "hr_status" && x.To != EmploymentStatus.Active);

        run.ErrorsJson = JsonSerializer.Serialize(errors.Take(200));
        run.Outcome = errors.Count == 0 ? "succeeded" : "succeeded_with_rejections";
        run.FinishedAt = DateTimeOffset.UtcNow;
        db.EmployeeSyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        // People who left (or went inactive) while holding a SIM: custody must be recovered by a person.
        var leaverIds = statusChanges.Where(x => x.To != EmploymentStatus.Active).Select(x => x.Employee.Id).Distinct().ToArray();
        var holders = await db.Assignments.AsNoTracking().Include(x => x.Sim).Include(x => x.Employee)
            .Where(x => x.ReturnedAt == null && leaverIds.Contains(x.EmployeeId))
            .Select(x => new { assignmentId = x.Id, simId = x.SimId, x.Sim.PhoneNumber, x.Employee.EmployeeNumber, x.Employee.FullName })
            .ToListAsync(cancellationToken);

        foreach (var change in statusChanges)
        {
            await AuditWriter.WriteAsync(db, actor, "employee_status_changed", "employee", change.Employee.Id.ToString(),
                JsonSerializer.Serialize(new
                {
                    change.Employee.EmployeeNumber, from = change.From.ToString(), to = change.To.ToString(), reason = change.Reason,
                    syncRunId = run.Id, holdsActiveSim = holders.Any(h => h.EmployeeNumber == change.Employee.EmployeeNumber)
                }));
        }
        await AuditWriter.WriteAsync(db, actor, "employee_directory_synced", "employee_sync_run", run.Id.ToString(),
            JsonSerializer.Serialize(new
            {
                run.Source, run.Mode, run.Received, run.Created, run.Updated, run.Deactivated, run.Unchanged, run.Rejected,
                holdersNeedingRecovery = holders.Count
            }));
        await transaction.CommitAsync(cancellationToken);
        return new SyncOutcome(run, holders);
    }

    public static HrDirectoryPayload FromCsv(string csv, string mode)
    {
        var table = SimpleCsv.Parse(csv);
        foreach (var required in new[] { "employee_number", "full_name", "department", "status" })
            if (!table.Headers.Contains(required))
                throw new FormatException($"The CSV must contain the column '{required}'. Expected: employee_number, full_name, email, department, cost_centre, status.");
        var records = table.Rows.Select(r => new HrEmployeeRecord(
            r.GetValueOrDefault("employee_number"), r.GetValueOrDefault("full_name"), r.GetValueOrDefault("email"),
            r.GetValueOrDefault("department"), r.GetValueOrDefault("cost_centre"), r.GetValueOrDefault("status"))).ToList();
        return new HrDirectoryPayload(mode, records);
    }

    public static bool TryParseStatus(string? value, out EmploymentStatus status)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_"))
        {
            case "active": case "employed": case "current":
                status = EmploymentStatus.Active; return true;
            case "inactive": case "on_leave": case "suspended": case "secondment":
                status = EmploymentStatus.Inactive; return true;
            case "terminated": case "separated": case "resigned": case "retired": case "left": case "exited":
                status = EmploymentStatus.Terminated; return true;
            default:
                status = EmploymentStatus.Inactive; return false;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
