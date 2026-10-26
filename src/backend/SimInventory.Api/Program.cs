using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SimInventory.Api.Data;
using SimInventory.Api.Models;
using SimInventory.Api.Security;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<TotpSecretProtector>();

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
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Session", policy => policy.RequireAuthenticatedUser()
        .RequireClaim("stage", "session"));
    options.AddPolicy("MfaSetup", policy => policy.RequireAuthenticatedUser()
        .RequireClaim("stage", "mfa_setup"));
    options.AddPolicy("MfaChallenge", policy => policy.RequireAuthenticatedUser()
        .RequireClaim("stage", "mfa_otp"));
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

app.UseHttpsRedirection();
app.UseCors("Frontend");
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
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "sim-inventory-api" }));

app.MapPost("/api/auth/google", async (
    GoogleCredentialRequest request, IConfiguration configuration, SimInventoryDbContext db,
    TokenService tokens, TotpSecretProtector protector, IHostEnvironment environment) =>
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
        if (string.IsNullOrWhiteSpace(bootstrapEmail) || email != bootstrapEmail)
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

    if (user.TotpEnabled)
        return Results.Ok(new AuthResponse("mfa_required", tokens.CreateToken(user, "mfa_otp", 5), user.Email, user.DisplayName));

    if (string.IsNullOrWhiteSpace(user.ProtectedTotpSecret))
    {
        user.ProtectedTotpSecret = protector.Protect(TotpHelper.CreateSecret());
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    var secret = protector.Unprotect(user.ProtectedTotpSecret);
    return Results.Ok(new AuthResponse("mfa_setup_required", tokens.CreateToken(user, "mfa_setup", 10),
        user.Email, user.DisplayName, user.Role.ToString(), TotpHelper.SetupUri(user.Email, secret), secret));
});

app.MapPost("/api/auth/mfa/setup", async (TotpRequest request, ClaimsPrincipal principal,
    SimInventoryDbContext db, TokenService tokens, TotpSecretProtector protector) =>
{
    var user = await CurrentUserAsync(principal, db);
    if (user is null || user.TotpEnabled || string.IsNullOrWhiteSpace(user.ProtectedTotpSecret))
        return Results.Unauthorized();
    var secret = protector.Unprotect(user.ProtectedTotpSecret);
    if (!TotpHelper.Verify(secret, request.Code))
        return Results.BadRequest(new { message = "Invalid or expired code. Try the current code in your authenticator app." });
    user.TotpEnabled = true;
    user.UpdatedAt = DateTimeOffset.UtcNow;
    await AuditWriter.WriteAsync(db, principal, "mfa_enabled", "user", user.Id.ToString(), "{}");
    await db.SaveChangesAsync();
    return Results.Ok(new AuthResponse("authenticated", tokens.CreateToken(user), user.Email, user.DisplayName, user.Role.ToString()));
}).RequireAuthorization("MfaSetup");

app.MapPost("/api/auth/otp", async (TotpRequest request, ClaimsPrincipal principal,
    SimInventoryDbContext db, TokenService tokens, TotpSecretProtector protector) =>
{
    var user = await CurrentUserAsync(principal, db);
    if (user is null || !user.TotpEnabled || string.IsNullOrWhiteSpace(user.ProtectedTotpSecret))
        return Results.Unauthorized();
    if (!TotpHelper.Verify(protector.Unprotect(user.ProtectedTotpSecret), request.Code))
        return Results.BadRequest(new { message = "Invalid or expired one-time code." });
    return Results.Ok(new AuthResponse("authenticated", tokens.CreateToken(user), user.Email, user.DisplayName, user.Role.ToString()));
}).RequireAuthorization("MfaChallenge");

app.MapGet("/api/auth/me", async (ClaimsPrincipal principal, SimInventoryDbContext db) =>
{
    var user = await CurrentUserAsync(principal, db);
    return user is null ? Results.Unauthorized() : Results.Ok(new
    {
        user.Id, user.Email, user.DisplayName, role = user.Role.ToString(), user.IsActive, user.TotpEnabled
    });
}).RequireAuthorization("Session");

app.MapGet("/api/admin/users", async (SimInventoryDbContext db) =>
    Results.Ok(await db.Users.AsNoTracking().OrderBy(x => x.Email)
        .Select(x => new { x.Id, x.Email, x.DisplayName, role = x.Role.ToString(), x.IsActive, x.TotpEnabled, x.CreatedAt })
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
    return Results.Ok(new { target.Id, target.Email, target.DisplayName, role = target.Role.ToString(), target.IsActive, target.TotpEnabled });
}).RequireAuthorization("Administrators");

app.MapGet("/api/admin/audit", async (SimInventoryDbContext db) =>
    Results.Ok(await db.AuditEvents.AsNoTracking().OrderByDescending(x => x.Id).Take(500)
        .Select(x => new { x.Id, x.ActorEmail, x.Action, x.EntityType, x.EntityId, x.Details, x.CreatedAt, x.PreviousHash, x.Hash })
        .ToListAsync())).RequireAuthorization("Administrators");

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

static Task<AppUser?> CurrentUserAsync(ClaimsPrincipal principal, SimInventoryDbContext db)
{
    var idText = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.TryParse(idText, out var id)
        ? db.Users.SingleOrDefaultAsync(x => x.Id == id && x.IsActive)
        : Task.FromResult<AppUser?>(null);
}

app.Run();

public partial class Program { }
