using Microsoft.EntityFrameworkCore;
using SimInventory.Api.Data;
using SimInventory.Api.Models;

namespace SimInventory.Api;

/// <summary>
/// Adds clearly synthetic records to an empty demo inventory without deleting or
/// overwriting existing employee, SIM, or assignment data.
/// </summary>
public static class DemoDataSeeder
{
    private static readonly string[] Departments =
    [
        "Operations", "Finance", "Human Resources", "Information Technology",
        "Procurement", "Customer Service"
    ];

    public static async Task SeedAsync(SimInventoryDbContext db)
    {
        var costCentres = new[] { "OPS-014", "FIN-021", "HR-008", "ICT-003", "PROC-011", "CS-017" };

        for (var index = 1; index <= 30; index++)
        {
            var employeeNumber = $"DEMO-EMP-{index:000}";
            if (!await db.Employees.AnyAsync(x => x.EmployeeNumber == employeeNumber))
            {
                var departmentIndex = (index - 1) % Departments.Length;
                db.Employees.Add(new Employee
                {
                    EmployeeNumber = employeeNumber,
                    FullName = $"Demo Employee {index:000}",
                    Email = $"employee{index:000}@example.test",
                    Department = Departments[departmentIndex],
                    CostCentre = costCentres[departmentIndex],
                    Status = EmploymentStatus.Active
                });
            }

            var iccid = $"DEMO-ICCID-{index:000000}";
            var phoneNumber = $"DEMO-NUMBER-{index:0000}";
            if (!await db.Sims.AnyAsync(x => x.Iccid == iccid || x.PhoneNumber == phoneNumber))
            {
                var operatorName = index % 3 switch
                {
                    0 => "Safaricom",
                    1 => "Airtel",
                    _ => "Telkom Kenya"
                };
                var departmentIndex = (index - 1) % Departments.Length;
                db.Sims.Add(new Sim
                {
                    Operator = operatorName,
                    PhoneNumber = phoneNumber,
                    Iccid = iccid,
                    PlanType = index % 4 switch
                    {
                        0 => "Corporate voice",
                        1 => "Corporate voice + data",
                        2 => "Data bundle",
                        _ => "Field operations"
                    },
                    MonthlyCost = index % 10 == 0 ? 4800m : 900m + (index % 6) * 250m,
                    ContractStart = new DateOnly(2025, 1, 1).AddDays(index * 7),
                    ContractEnd = index % 5 == 0
                        ? new DateOnly(2026, 6, 30)
                        : new DateOnly(2027, 12, 31),
                    Status = SimStatus.Available,
                    CostCentre = costCentres[departmentIndex]
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
