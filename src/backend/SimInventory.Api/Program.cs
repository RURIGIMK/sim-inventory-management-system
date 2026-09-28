using Microsoft.EntityFrameworkCore;
using SimInventory.Api.Data;
using SimInventory.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddDbContext<SimInventoryDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "sim-inventory-api"
}));

app.MapGet("/api/sims", async (SimInventoryDbContext db) =>
    Results.Ok(await db.Sims.AsNoTracking().ToListAsync()));

app.MapPost("/api/sims", async (Sim sim, SimInventoryDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(sim.Iccid) || string.IsNullOrWhiteSpace(sim.PhoneNumber))
        return Results.BadRequest(new { message = "ICCID and phone number are required." });

    if (await db.Sims.AnyAsync(x => x.Iccid == sim.Iccid))
        return Results.Conflict(new { message = "A SIM with this ICCID already exists." });

    if (await db.Sims.AnyAsync(x => x.PhoneNumber == sim.PhoneNumber))
        return Results.Conflict(new { message = "A SIM with this phone number already exists." });

    sim.Id = 0;
    sim.Status = SimStatus.Available;

    db.Sims.Add(sim);
    await db.SaveChangesAsync();

    return Results.Created($"/api/sims/{sim.Id}", sim);
});

app.Run();

public partial class Program { }
