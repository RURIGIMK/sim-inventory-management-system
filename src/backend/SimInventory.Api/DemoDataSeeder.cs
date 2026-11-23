using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SimInventory.Api.Models;
using SimInventory.Api.Security;

namespace SimInventory.Api.Data;

/// <summary>
/// Demonstration data. Names are realistic but fictional: they are common Kenyan first names and
/// surnames combined at random and do not describe any real employee. Phone numbers and ICCIDs stay
/// visibly synthetic because a realistic-looking number could belong to a real subscriber.
/// Nothing here deletes data; existing records are updated in place so their history is preserved.
/// </summary>
public static class DemoDataSeeder
{
    private static readonly string[] Departments =
    [
        "Operations", "Finance", "Human Resources", "Information Technology",
        "Procurement", "Customer Service"
    ];

    private static readonly string[] CostCentres = ["OPS-014", "FIN-021", "HR-008", "ICT-003", "PROC-011", "CS-017"];

    public static readonly string[] EmployeeNames =
    [
        "Wanjiru Kamau", "Brian Otieno", "Grace Achieng", "Kevin Kiprono", "Fatuma Hassan",
        "Peter Mwangi", "Mercy Chebet", "Dennis Ouma", "Faith Wambui", "Samuel Kiptoo",
        "Halima Abdi", "Joseph Mutua", "Esther Nyambura", "Collins Wekesa", "Purity Wanjiku",
        "Daniel Kimani", "Lilian Akinyi", "Victor Mutiso", "Naomi Jeptoo", "Ibrahim Omar",
        "Caroline Njeri", "Martin Ochieng", "Sharon Moraa", "Paul Kirui", "Agnes Wairimu",
        "George Onyango", "Ruth Chepkoech", "Stephen Muriuki", "Janet Atieno", "Hassan Mohamed"
    ];

    public static string EmployeeNumberFor(int index) => $"EMP-{10000 + index * 13}";

    public static string EmailFor(string fullName) =>
        fullName.ToLowerInvariant().Replace(' ', '.') + "@staff.example";

    public static async Task<SeedSummary> SeedAsync(SimInventoryDbContext db, bool includeBilling, DateOnly today)
    {
        var summary = new SeedSummary();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var employees = await db.Employees.ToListAsync();
        var byNumber = employees.ToDictionary(x => x.EmployeeNumber, StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index <= EmployeeNames.Length; index++)
        {
            var departmentIndex = (index - 1) % Departments.Length;
            var name = EmployeeNames[index - 1];
            var number = EmployeeNumberFor(index);
            var employee = byNumber.GetValueOrDefault(number) ?? byNumber.GetValueOrDefault($"DEMO-EMP-{index:000}");
            if (employee is null)
            {
                db.Employees.Add(new Employee
                {
                    EmployeeNumber = number, FullName = name, Email = EmailFor(name),
                    Department = Departments[departmentIndex], CostCentre = CostCentres[departmentIndex],
                    Status = EmploymentStatus.Active, Source = "seed"
                });
                summary.EmployeesCreated++;
            }
            else if (employee.EmployeeNumber != number || employee.FullName != name)
            {
                // Earlier versions used placeholder names such as "Demo Employee 001". The row keeps its
                // Id, so assignment history stays attached to the same person.
                employee.EmployeeNumber = number;
                employee.FullName = name;
                employee.Email = EmailFor(name);
                employee.UpdatedAt = DateTimeOffset.UtcNow;
                summary.EmployeesRenamed++;
            }
        }

        var existingIccids = (await db.Sims.Select(x => x.Iccid).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingPhones = (await db.Sims.Select(x => x.PhoneNumber).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index <= 30; index++)
        {
            var iccid = $"DEMO-ICCID-{index:000000}";
            var phoneNumber = $"DEMO-NUMBER-{index:0000}";
            if (existingIccids.Contains(iccid) || existingPhones.Contains(phoneNumber)) continue;
            var departmentIndex = (index - 1) % Departments.Length;
            db.Sims.Add(new Sim
            {
                Operator = (index % 3) switch { 0 => "Safaricom", 1 => "Airtel", _ => "Telkom Kenya" },
                PhoneNumber = phoneNumber,
                Iccid = iccid,
                PlanType = (index % 4) switch
                {
                    0 => "Corporate voice", 1 => "Corporate voice + data", 2 => "Data bundle", _ => "Field operations"
                },
                MonthlyCost = index % 10 == 0 ? 4800m : 900m + (index % 6) * 250m,
                ContractStart = new DateOnly(2025, 1, 1).AddDays(index * 7),
                ContractEnd = index % 5 == 0 ? new DateOnly(2026, 6, 30) : new DateOnly(2027, 12, 31),
                Status = SimStatus.Available,
                CostCentre = CostCentres[departmentIndex]
            });
            summary.SimsCreated++;
        }
        await db.SaveChangesAsync();

        if (includeBilling)
            summary.CostRecordsCreated = await SeedSyntheticBillingAsync(db, today);

        if (summary.HasChanges)
        {
            await AuditWriter.WriteAsync(db, new ClaimsPrincipal(), "demo_data_seeded", "system", "demo-seed",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    summary.EmployeesCreated, summary.EmployeesRenamed, summary.SimsCreated, summary.CostRecordsCreated,
                    dataClassification = "synthetic"
                }));
        }
        await transaction.CommitAsync();
        return summary;
    }

    /// <summary>
    /// Generates deterministic synthetic invoice lines with labelled anomaly scenarios.
    /// The scenario label is stored for evaluation only and is never a model feature.
    /// </summary>
    public static async Task<int> SeedSyntheticBillingAsync(SimInventoryDbContext db, DateOnly today)
    {
        var lastPeriod = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var firstPeriod = new DateOnly(2025, 6, 1);
        var sims = await db.Sims.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var existing = (await db.CostRecords.AsNoTracking().Select(x => new { x.SimId, x.Period }).ToListAsync())
            .Select(x => (x.SimId, x.Period)).ToHashSet();
        var batch = Guid.NewGuid();
        var created = 0;

        for (var position = 0; position < sims.Count; position++)
        {
            var sim = sims[position];
            if (sim.Status == SimStatus.Deactivated) continue;
            var start = sim.ContractStart is { } cs ? new DateOnly(cs.Year, cs.Month, 1) : firstPeriod;
            if (start < firstPeriod) start = firstPeriod;
            var periods = new List<DateOnly>();
            for (var p = start; p <= lastPeriod; p = p.AddMonths(1)) periods.Add(p);
            if (periods.Count == 0) continue;

            var random = new Random(4242 + sim.Id);
            var fee = (double)sim.MonthlyCost;
            var (dataBase, voiceBase) = sim.PlanType.ToLowerInvariant() switch
            {
                var t when t.Contains("data bundle") => (9000.0, 40.0),
                var t when t.Contains("voice + data") => (4500.0, 420.0),
                var t when t.Contains("field") => (2500.0, 650.0),
                _ => (600.0, 900.0)
            };
            var scenarios = ScenarioPlan(position, periods.Count);

            for (var i = 0; i < periods.Count; i++)
            {
                if (existing.Contains((sim.Id, periods[i]))) continue;
                var amount = fee * (random.NextDouble() < 0.3 ? 1 + random.NextDouble() * 0.08 : 1.0);
                var data = dataBase * (0.7 + random.NextDouble() * 0.6);
                var voice = voiceBase * (0.7 + random.NextDouble() * 0.6);
                string? scenario = scenarios.TryGetValue(i, out var planned) ? planned : null;
                switch (scenario)
                {
                    case "roaming_spike":
                        amount = fee * (2.4 + random.NextDouble() * 1.4);
                        data *= 3 + random.NextDouble() * 3;
                        break;
                    case "plan_mismatch":
                        amount = fee * (1.35 + random.NextDouble() * 0.3);
                        data *= 1.8 + random.NextDouble() * 0.5;
                        break;
                    case "dormant_billed":
                        amount = fee;
                        data = 0;
                        voice = 0;
                        break;
                    case "duplicate_charge":
                        amount = fee * 2 + (amount - fee);
                        break;
                }

                db.CostRecords.Add(new SimCostRecord
                {
                    SimId = sim.Id, Period = periods[i], Amount = Math.Round((decimal)amount, 2),
                    DataUsageMb = (int)Math.Round(data), VoiceMinutes = (int)Math.Round(voice),
                    Source = "synthetic", SyntheticScenario = scenario, ImportBatchId = batch
                });
                created++;
            }
        }

        await db.SaveChangesAsync();
        return created;
    }

    /// <summary>Which months of which SIM carry an injected scenario (by SIM position and month index).</summary>
    private static Dictionary<int, string> ScenarioPlan(int position, int months)
    {
        var plan = new Dictionary<int, string>();
        void Mark(string scenario, int fromEnd, int length)
        {
            var startIndex = months - fromEnd;
            for (var k = 0; k < length; k++)
                if (startIndex + k >= 0 && startIndex + k < months) plan[startIndex + k] = scenario;
        }

        switch (position)
        {
            case 2: Mark("roaming_spike", 11, 1); break;
            case 11: Mark("roaming_spike", 3, 1); break;
            case 19: Mark("roaming_spike", 7, 1); break;
            case 5: Mark("plan_mismatch", 6, 5); break;
            case 23: Mark("plan_mismatch", 9, 5); break;
            case 8: Mark("dormant_billed", 4, 4); break;
            case 16: Mark("dormant_billed", 10, 4); break;
            case 13: Mark("duplicate_charge", 2, 1); break;
            case 27: Mark("duplicate_charge", 8, 1); break;
        }
        return plan;
    }

    public sealed class SeedSummary
    {
        public int EmployeesCreated { get; set; }
        public int EmployeesRenamed { get; set; }
        public int SimsCreated { get; set; }
        public int CostRecordsCreated { get; set; }
        public bool HasChanges => EmployeesCreated + EmployeesRenamed + SimsCreated + CostRecordsCreated > 0;
    }
}
