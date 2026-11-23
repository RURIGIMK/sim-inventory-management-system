using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SimInventory.Api.Data;
using SimInventory.Api.Models;
using SimInventory.Api.Security;
using Testcontainers.PostgreSql;
using Xunit;

namespace SimInventory.Api.Tests.Integration;

/// <summary>
/// Runs the real API against a throwaway PostgreSQL 16 container, migrated with the same
/// versioned scripts as production. Tests share one database and run sequentially.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string IntegrationToken = "integration-token-for-tests-0123456789abcdef";
    public const string WebhookSecret = "hr-webhook-secret-for-tests-0123456789abcdef";
    private const string SigningKey = "integration-test-signing-key-0123456789-abcdefghijkl";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Environment variables are read by WebApplication.CreateBuilder before Program.cs reads configuration.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Authentication__JwtSigningKey", SigningKey);
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup", "true");
        Environment.SetEnvironmentVariable("Database__SeedDemoData", "false");
        Environment.SetEnvironmentVariable("Integration__Token", IntegrationToken);
        Environment.SetEnvironmentVariable("Hr__WebhookSecret", WebhookSecret);
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        using var warmup = Factory.CreateClient();
        (await warmup.GetAsync("/api/health")).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public async Task<T> WithDbAsync<T>(Func<SimInventoryDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<SimInventoryDbContext>());
    }

    public Task WithDbAsync(Func<SimInventoryDbContext, Task> action) =>
        WithDbAsync(async db => { await action(db); return true; });

    public async Task<(AppUser User, HttpClient Client)> UserAsync(UserRole role, string? department = null)
    {
        var user = new AppUser
        {
            Email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@example.test",
            DisplayName = role.ToString(), Role = role, Department = department
        };
        await WithDbAsync(async db => { db.Users.Add(user); await db.SaveChangesAsync(); });
        using var scope = Factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<TokenService>().CreateToken(user);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (user, client);
    }

    public HttpClient IntegrationClient()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Integration-Token", IntegrationToken);
        return client;
    }

    public Task<Employee> EmployeeAsync(string department, EmploymentStatus status = EmploymentStatus.Active) =>
        WithDbAsync(async db =>
        {
            var employee = new Employee
            {
                EmployeeNumber = "T-" + Guid.NewGuid().ToString("N")[..10], FullName = "Test Person",
                Department = department, CostCentre = "CC-1", Status = status, Source = "test"
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            return employee;
        });

    public Task<Sim> SimAsync(decimal monthlyCost = 1000m) =>
        WithDbAsync(async db =>
        {
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var sim = new Sim
            {
                Operator = "Safaricom", PhoneNumber = "TEST-" + suffix, Iccid = "TESTICCID-" + suffix,
                PlanType = "Corporate voice + data", MonthlyCost = monthlyCost, CostCentre = "CC-1", Status = SimStatus.Available
            };
            db.Sims.Add(sim);
            await db.SaveChangesAsync();
            return sim;
        });
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

public static class HttpExtensions
{
    public static Task<HttpResponseMessage> PostJson<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body);
}
