using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SimInventory.Api.Data;
using SimInventory.Api.Models;
using SimInventory.Api.Intelligence;
using SimInventory.Api.Security;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<TokenService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
builder.Services.AddDbContext<SimInventoryDbContext>(options => options.UseNpgsql(connectionString));

var signingKey = builder.Configuration["Authentication:JwtSigningKey"];
if (string.IsNullOrWhiteSpace(signingKey) && builder.Environment.IsDevelopment())
    signingKey = "LOCAL-ONLY-DO-NOT-USE-IN-PRODUCTION-KEY-CHANGE-ME-123456789";
if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
    throw new InvalidOperationException("Configure Authentication:JwtSigningKey with a random value of at least 32 bytes.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Authentication:Issuer"] ?? "sim-inventory",
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Authentication:Audience"] ?? "sim-inventory-client",
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var userIdText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdText, out var userId))
            {
                context.Fail("Invalid account.");
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<SimInventoryDbContext>();
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId);
            if (user is null || !user.IsActive)
            {
                context.Fail("This account is disabled or no longer exists.");
                return;
            }

            var identity = context.Principal?.Identity as ClaimsIdentity;
            if (identity is not null)
            {
                foreach (var roleClaim in identity.FindAll(ClaimTypes.Role).ToArray())
                    identity.RemoveClaim(roleClaim);
                identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
            }
        }
    };
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Session", policy => policy.RequireAuthenticatedUser()
        .RequireClaim("stage", "session"));
    options.AddPolicy("Managers", policy => policy.RequireAuthenticatedUser()
        .RequireClaim("stage", "session")
        .RequireRole(nameof(UserRole.Administrator), nameof(UserRole.DepartmentalManager)));
    options.AddPolicy("Administrators", policy => policy.RequireAuthenticatedUser()
        .RequireClaim("stage", "session").RequireRole(nameof(UserRole.Administrator)));
});

var origins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:3000")
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// The hosting platform terminates public HTTPS; local development may use HTTP.
app.UseRouting();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// For the current prototype, create missing tables on first start. Replace with
// reviewed EF Core migrations before any production or real-data deployment.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SimInventoryDbContext>();
    await db.Database.EnsureCreatedAsync();

    // EnsureCreated does not add tables to an existing database. These idempotent
    // statements keep older local demo databases usable until migrations replace this bootstrap.
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "Users" (
            "Id" uuid PRIMARY KEY, "Email" varchar(320) NOT NULL, "DisplayName" varchar(160) NOT NULL,
            "Role" varchar(40) NOT NULL, "IsActive" boolean NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL, "DisabledAt" timestamptz NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");
        """);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "Employees" (
            "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "EmployeeNumber" varchar(80) NOT NULL,
            "FullName" varchar(200) NOT NULL, "Email" varchar(320) NULL, "Department" varchar(120) NOT NULL,
            "CostCentre" varchar(80) NOT NULL, "Status" varchar(30) NOT NULL, "CreatedAt" timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Employees_EmployeeNumber" ON "Employees" ("EmployeeNumber");
        """);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "Assignments" (
            "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            "SimId" integer NOT NULL REFERENCES "Sims" ("Id") ON DELETE RESTRICT,
            "EmployeeId" integer NOT NULL REFERENCES "Employees" ("Id") ON DELETE RESTRICT,
            "AssignedByUserId" uuid NOT NULL, "AssignedAt" timestamptz NOT NULL,
            "ReturnedAt" timestamptz NULL, "ReturnedByUserId" uuid NULL, "Notes" varchar(1000) NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Assignments_SimId" ON "Assignments" ("SimId") WHERE "ReturnedAt" IS NULL;
        CREATE INDEX IF NOT EXISTS "IX_Assignments_EmployeeId" ON "Assignments" ("EmployeeId");
        """);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "AuditEvents" (
            "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "ActorUserId" uuid NULL,
            "ActorEmail" varchar(320) NOT NULL, "Action" varchar(100) NOT NULL,
            "EntityType" varchar(100) NOT NULL, "EntityId" varchar(200) NOT NULL, "Details" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL, "PreviousHash" varchar(64) NOT NULL, "Hash" varchar(64) NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_AuditEvents_CreatedAt" ON "AuditEvents" ("CreatedAt");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_AuditEvents_Hash" ON "AuditEvents" ("Hash");
        """);
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "sim-inventory-api" }));

app.MapPost("/api/auth/google", async (
    GoogleCredentialRequest request, IConfiguration configuration, SimInventoryDbContext db,
    TokenService tokens) =>
{
    var clientId = configuration["Authentication:GoogleClientId"];
    if (string.IsNullOrWhiteSpace(clientId))
        return Results.Problem("Google sign-in is not configured. Set Authentication:GoogleClientId.", statusCode: 503);

    GoogleJsonWebSignature.Payload payload;
    try
    {
        payload = await GoogleJsonWebSignature.ValidateAsync(request.Credential,
            new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
    }
    catch (Exception)
    {
        return Results.Unauthorized();
    }

    if (payload.EmailVerified != true || string.IsNullOrWhiteSpace(payload.Email))
        return Results.Unauthorized();

    var email = payload.Email.Trim().ToLowerInvariant();
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
    if (user is null)
    {
        var bootstrapEmail = configuration["Authentication:BootstrapAdminEmail"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(bootstrapEmail) || email != bootstrapEmail || await db.Users.AnyAsync())
            return Results.Json(new { message = "This Google account has not been provisioned. Ask an administrator to create it." },
                statusCode: StatusCodes.Status403Forbidden);

        user = new AppUser
        {
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(payload.Name) ? email : payload.Name,
            Role = UserRole.Administrator,
            IsActive = true
        };
        db.Users.Add(user);
        await AuditWriter.WriteAsync(db, new ClaimsPrincipal(), "bootstrap_admin_created", "user", user.Id.ToString(), "{\"source\":\"configured bootstrap email\"}");
        await db.SaveChangesAsync();
    }

    if (!user.IsActive)
        return Results.Json(new { message = "This account has been disabled. Contact an administrator." },
            statusCode: StatusCodes.Status403Forbidden);

    var auditActor = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Email, user.Email)
    }));
    await AuditWriter.WriteAsync(db, auditActor, "login_successful", "user", user.Id.ToString(), "{}");
    await db.SaveChangesAsync();

    return Results.Ok(new AuthResponse("authenticated",
        tokens.CreateToken(user), user.Email, user.DisplayName, user.Role.ToString()));
}).RequireRateLimiting("auth");

app.MapGet("/api/auth/me", async (ClaimsPrincipal principal, SimInventoryDbContext db) =>
{
    var user = await CurrentUserAsync(principal, db);
    return user is null ? Results.Unauthorized() : Results.Ok(new
    {
        user.Id, user.Email, user.DisplayName, role = user.Role.ToString(), user.IsActive
    });
}).RequireAuthorization("Session");

app.MapGet("/api/admin/users", async (SimInventoryDbContext db) =>
    Results.Ok(await db.Users.AsNoTracking().OrderBy(x => x.Email)
        .Select(x => new { x.Id, x.Email, x.DisplayName, role = x.Role.ToString(), x.IsActive, x.CreatedAt })
        .ToListAsync())).RequireAuthorization("Administrators");

app.MapPost("/api/admin/users", async (CreateUserRequest request, ClaimsPrincipal principal, SimInventoryDbContext db) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || string.IsNullOrWhiteSpace(request.DisplayName))
        return Results.BadRequest(new { message = "A valid email and display name are required." });
    if (!Enum.IsDefined(request.Role)) return Results.BadRequest(new { message = "Invalid role." });
    if (await db.Users.AnyAsync(x => x.Email == email))
        return Results.Conflict(new { message = "An account for this email already exists." });

    var user = new AppUser { Email = email, DisplayName = request.DisplayName.Trim(), Role = request.Role };
    db.Users.Add(user);
    await AuditWriter.WriteAsync(db, principal, "user_created", "user", user.Id.ToString(),
        System.Text.Json.JsonSerializer.Serialize(new { user.Email, role = user.Role.ToString() }));
    await db.SaveChangesAsync();
    return Results.Created($"/api/admin/users/{user.Id}", new { user.Id, user.Email, user.DisplayName, role = user.Role.ToString(), user.IsActive });
}).RequireAuthorization("Administrators");

app.MapPut("/api/admin/users/{id:guid}", async (Guid id, UpdateUserRequest request, ClaimsPrincipal principal, SimInventoryDbContext db) =>
{
    var target = await db.Users.SingleOrDefaultAsync(x => x.Id == id);
    if (target is null) return Results.NotFound();
    if (!Enum.IsDefined(request.Role)) return Results.BadRequest(new { message = "Invalid role." });
    var actorId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
    if (target.Id == actorId && (!request.IsActive || request.Role != UserRole.Administrator))
        return Results.BadRequest(new { message = "You cannot disable your own account or remove your own administrator role." });

    if (target.Role == UserRole.Administrator && target.IsActive &&
        (!request.IsActive || request.Role != UserRole.Administrator) &&
        await db.Users.CountAsync(x => x.IsActive && x.Role == UserRole.Administrator) <= 1)
        return Results.BadRequest(new { message = "The last active administrator cannot be disabled or demoted." });

    var before = new { role = target.Role.ToString(), target.IsActive };
    target.Role = request.Role;
    target.IsActive = request.IsActive;
    target.DisabledAt = request.IsActive ? null : DateTimeOffset.UtcNow;
    target.UpdatedAt = DateTimeOffset.UtcNow;
    await AuditWriter.WriteAsync(db, principal, "user_updated", "user", target.Id.ToString(),
        System.Text.Json.JsonSerializer.Serialize(new { before, after = new { role = target.Role.ToString(), target.IsActive } }));
    await db.SaveChangesAsync();
    return Results.Ok(new { target.Id, target.Email, target.DisplayName, role = target.Role.ToString(), target.IsActive });
}).RequireAuthorization("Administrators");

app.MapGet("/api/admin/audit", async (SimInventoryDbContext db) =>
    Results.Ok(await db.AuditEvents.AsNoTracking().OrderByDescending(x => x.Id).Take(500)
        .Select(x => new { x.Id, x.ActorEmail, x.Action, x.EntityType, x.EntityId, x.Details, x.CreatedAt, x.PreviousHash, x.Hash })
        .ToListAsync())).RequireAuthorization("Administrators");

app.MapGet("/api/intelligence/alerts", async (SimInventoryDbContext db) =>
{
    var sims = await db.Sims.AsNoTracking().ToListAsync();
    var assignments = await db.Assignments.AsNoTracking().ToListAsync();
    var employees = await db.Employees.AsNoTracking().ToListAsync();
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    return Results.Ok(SimAssetIntelligence.Evaluate(sims, assignments, employees, today));
}).RequireAuthorization("Session");

app.MapGet("/api/sims", async (SimInventoryDbContext db) =>
    Results.Ok(await db.Sims.AsNoTracking().OrderBy(x => x.Id).ToListAsync()))
    .RequireAuthorization("Session");

app.MapPost("/api/sims", async (Sim sim, ClaimsPrincipal principal, SimInventoryDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(sim.Iccid) || string.IsNullOrWhiteSpace(sim.PhoneNumber))
        return Results.BadRequest(new { message = "ICCID and phone number are required." });
    if (sim.MonthlyCost < 0)
        return Results.BadRequest(new { message = "Monthly cost cannot be negative." });
    sim.Iccid = sim.Iccid.Trim();
    sim.PhoneNumber = sim.PhoneNumber.Trim();
    if (await db.Sims.AnyAsync(x => x.Iccid == sim.Iccid))
        return Results.Conflict(new { message = "A SIM with this ICCID already exists." });
    if (await db.Sims.AnyAsync(x => x.PhoneNumber == sim.PhoneNumber))
        return Results.Conflict(new { message = "A SIM with this phone number already exists." });

    sim.Id = 0;
    sim.Status = SimStatus.Available;
    db.Sims.Add(sim);
    await db.SaveChangesAsync();
    await AuditWriter.WriteAsync(db, principal, "sim_registered", "sim", sim.Id.ToString(),
        System.Text.Json.JsonSerializer.Serialize(new { sim.Iccid, sim.PhoneNumber, sim.Operator }));
    await db.SaveChangesAsync();
    return Results.Created($"/api/sims/{sim.Id}", sim);
}).RequireAuthorization("Managers");


app.MapGet("/api/employees", async (SimInventoryDbContext db) =>
    Results.Ok(await db.Employees.AsNoTracking().OrderBy(x => x.EmployeeNumber)
        .Select(x => new { x.Id, x.EmployeeNumber, x.FullName, x.Email, x.Department, x.CostCentre, status = x.Status.ToString() })
        .ToListAsync())).RequireAuthorization("Session");

app.MapPost("/api/admin/employees", async (CreateEmployeeRequest request, ClaimsPrincipal principal, SimInventoryDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.EmployeeNumber) || string.IsNullOrWhiteSpace(request.FullName) ||
        string.IsNullOrWhiteSpace(request.Department) || string.IsNullOrWhiteSpace(request.CostCentre))
        return Results.BadRequest(new { message = "Employee number, full name, department and cost centre are required." });
    var employeeNumber = request.EmployeeNumber.Trim();
    if (await db.Employees.AnyAsync(x => x.EmployeeNumber == employeeNumber))
        return Results.Conflict(new { message = "An employee with this employee number already exists." });
    var employee = new Employee
    {
        EmployeeNumber = employeeNumber, FullName = request.FullName.Trim(),
        Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant(),
        Department = request.Department.Trim(), CostCentre = request.CostCentre.Trim(), Status = EmploymentStatus.Active
    };
    db.Employees.Add(employee);
    await AuditWriter.WriteAsync(db, principal, "employee_created", "employee", employee.Id.ToString(),
        System.Text.Json.JsonSerializer.Serialize(new { employee.EmployeeNumber, employee.Department, employee.CostCentre }));
    await db.SaveChangesAsync();
    return Results.Created($"/api/employees/{employee.Id}", new { employee.Id, employee.EmployeeNumber, employee.FullName, employee.Department, employee.CostCentre, status = employee.Status.ToString() });
}).RequireAuthorization("Administrators");

app.MapPut("/api/admin/employees/{id:int}/status", async (int id, UpdateEmployeeStatusRequest request, ClaimsPrincipal principal, SimInventoryDbContext db) =>
{
    if (!Enum.IsDefined(request.Status)) return Results.BadRequest(new { message = "Invalid employment status." });
    var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == id);
    if (employee is null) return Results.NotFound();
    if (request.Status != EmploymentStatus.Active && await db.Assignments.AnyAsync(x => x.EmployeeId == id && x.ReturnedAt == null))
        return Results.Conflict(new { message = "Return all currently assigned SIMs before deactivating or terminating this employee." });
    var before = employee.Status.ToString();
    employee.Status = request.Status;
    await AuditWriter.WriteAsync(db, principal, "employee_status_changed", "employee", employee.Id.ToString(),
        System.Text.Json.JsonSerializer.Serialize(new { before, after = request.Status.ToString() }));
    await db.SaveChangesAsync();
    return Results.Ok(new { employee.Id, status = employee.Status.ToString() });
}).RequireAuthorization("Administrators");

app.MapGet("/api/allocations", async (SimInventoryDbContext db) =>
    Results.Ok(await db.Assignments.AsNoTracking().Include(x => x.Sim).Include(x => x.Employee)
        .OrderByDescending(x => x.AssignedAt)
        .Select(x => new
        {
            x.Id, x.SimId, simPhoneNumber = x.Sim.PhoneNumber, simIccid = x.Sim.Iccid,
            x.EmployeeId, employeeNumber = x.Employee.EmployeeNumber, employeeName = x.Employee.FullName,
            x.AssignedAt, x.ReturnedAt, x.AssignedByUserId, x.ReturnedByUserId, x.Notes, isActive = x.ReturnedAt == null
        }).ToListAsync())).RequireAuthorization("Session");

app.MapPost("/api/allocations", async (AllocateSimRequest request, ClaimsPrincipal principal, SimInventoryDbContext db) =>
{
    var sim = await db.Sims.SingleOrDefaultAsync(x => x.Id == request.SimId);
    if (sim is null) return Results.NotFound(new { message = "SIM not found." });
    var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == request.EmployeeId);
    if (employee is null) return Results.NotFound(new { message = "Employee not found." });
    if (employee.Status != EmploymentStatus.Active)
        return Results.Conflict(new { message = "Only active employees can receive a SIM." });
    if (sim.Status != SimStatus.Available || await db.Assignments.AnyAsync(x => x.SimId == sim.Id && x.ReturnedAt == null))
        return Results.Conflict(new { message = "This SIM is not available for allocation." });
    var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var assignment = new SimAssignment { SimId = sim.Id, EmployeeId = employee.Id, AssignedByUserId = actorId, Notes = request.Notes?.Trim() ?? string.Empty };
    sim.Status = SimStatus.Allocated;
    db.Assignments.Add(assignment);
    await AuditWriter.WriteAsync(db, principal, "sim_allocated", "assignment", assignment.Id.ToString(),
        System.Text.Json.JsonSerializer.Serialize(new { simId = sim.Id, employeeId = employee.Id, employee.EmployeeNumber }));
    await db.SaveChangesAsync();
    return Results.Created($"/api/allocations/{assignment.Id}", new { assignment.Id, assignment.SimId, assignment.EmployeeId, assignment.AssignedAt });
}).RequireAuthorization("Managers");

app.MapPost("/api/allocations/{id:int}/return", async (int id, ClaimsPrincipal principal, SimInventoryDbContext db) =>
{
    var assignment = await db.Assignments.Include(x => x.Sim).SingleOrDefaultAsync(x => x.Id == id);
    if (assignment is null) return Results.NotFound();
    if (assignment.ReturnedAt is not null) return Results.Conflict(new { message = "This allocation has already been returned." });
    var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    assignment.ReturnedAt = DateTimeOffset.UtcNow;
    assignment.ReturnedByUserId = actorId;
    if (assignment.Sim.Status != SimStatus.Deactivated) assignment.Sim.Status = SimStatus.Available;
    await AuditWriter.WriteAsync(db, principal, "sim_returned", "assignment", assignment.Id.ToString(),
        System.Text.Json.JsonSerializer.Serialize(new { assignment.SimId, assignment.EmployeeId }));
    await db.SaveChangesAsync();
    return Results.Ok(new { assignment.Id, assignment.ReturnedAt, status = assignment.Sim.Status.ToString() });
}).RequireAuthorization("Managers");

app.MapPost("/api/allocations/{id:int}/reassign", async (int id, ReassignSimRequest request, ClaimsPrincipal principal, SimInventoryDbContext db) =>
{
    await using var transaction = await db.Database.BeginTransactionAsync();
    var previous = await db.Assignments.Include(x => x.Sim).SingleOrDefaultAsync(x => x.Id == id);
    if (previous is null) return Results.NotFound();
    if (previous.ReturnedAt is not null) return Results.Conflict(new { message = "Only an active allocation can be reassigned." });
    var nextEmployee = await db.Employees.SingleOrDefaultAsync(x => x.Id == request.EmployeeId);
    if (nextEmployee is null) return Results.NotFound(new { message = "Employee not found." });
    if (nextEmployee.Status != EmploymentStatus.Active)
        return Results.Conflict(new { message = "Only active employees can receive a SIM." });
    if (previous.EmployeeId == nextEmployee.Id)
        return Results.BadRequest(new { message = "Choose a different employee for reassignment." });

    var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    previous.ReturnedAt = DateTimeOffset.UtcNow;
    previous.ReturnedByUserId = actorId;
    var next = new SimAssignment
    {
        SimId = previous.SimId, EmployeeId = nextEmployee.Id, AssignedByUserId = actorId,
        Notes = request.Notes?.Trim() ?? string.Empty
    };
    db.Assignments.Add(next);
    previous.Sim.Status = SimStatus.Allocated;
    await AuditWriter.WriteAsync(db, principal, "sim_reassigned", "assignment", next.Id.ToString(),
        System.Text.Json.JsonSerializer.Serialize(new { simId = previous.SimId, fromEmployeeId = previous.EmployeeId, toEmployeeId = nextEmployee.Id }));
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    return Results.Created($"/api/allocations/{next.Id}", new { next.Id, next.SimId, next.EmployeeId, next.AssignedAt });
}).RequireAuthorization("Managers");

app.MapGet("/api/admin/audit/verify", async (SimInventoryDbContext db) =>
{
    var events = await db.AuditEvents.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
    var previous = "GENESIS";
    foreach (var entry in events)
    {
        var canonical = $"{previous}|{entry.CreatedAt:O}|{entry.ActorEmail}|{entry.Action}|{entry.EntityType}|{entry.EntityId}|{entry.Details}";
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        if (entry.PreviousHash != previous || entry.Hash != expected)
            return Results.Ok(new { valid = false, checkedEvents = entry.Id - 1, firstInvalidEventId = entry.Id });
        previous = entry.Hash;
    }
    return Results.Ok(new { valid = true, checkedEvents = events.Count, firstInvalidEventId = (long?)null });
}).RequireAuthorization("Administrators");

static Task<AppUser?> CurrentUserAsync(ClaimsPrincipal principal, SimInventoryDbContext db)
{
    var idText = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.TryParse(idText, out var id)
        ? db.Users.SingleOrDefaultAsync(x => x.Id == id && x.IsActive)
        : Task.FromResult<AppUser?>(null);
}

app.Run();

public partial class Program { }
