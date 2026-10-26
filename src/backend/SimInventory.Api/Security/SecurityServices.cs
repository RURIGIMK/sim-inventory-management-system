using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SimInventory.Api.Models;

namespace SimInventory.Api.Security;

public sealed class TokenService(IConfiguration configuration, IHostEnvironment environment)
{
    public string CreateToken(AppUser user, string stage = "session", int minutes = 60)
    {
        var key = configuration["Authentication:JwtSigningKey"];
        if (string.IsNullOrWhiteSpace(key) && environment.IsDevelopment())
            key = "LOCAL-ONLY-DO-NOT-USE-IN-PRODUCTION-KEY-CHANGE-ME-123456789";
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Set Authentication:JwtSigningKey to a random value of at least 32 bytes.");
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("stage", stage)
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: configuration["Authentication:Issuer"] ?? "sim-inventory",
            audience: configuration["Authentication:Audience"] ?? "sim-inventory-client",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}


public static class AuditWriter
{
    public static async Task WriteAsync(SimInventory.Api.Data.SimInventoryDbContext db,
        ClaimsPrincipal actor, string action, string entityType, string entityId, string details)
    {
        var previous = await db.AuditEvents.OrderByDescending(x => x.Id).Select(x => x.Hash).FirstOrDefaultAsync() ?? "GENESIS";
        var email = actor.FindFirstValue(ClaimTypes.Email) ?? "system";
        var actorIdValue = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? actorId = Guid.TryParse(actorIdValue, out var parsed) ? parsed : null;
        var created = DateTimeOffset.UtcNow;
        var canonical = $"{previous}|{created:O}|{email}|{action}|{entityType}|{entityId}|{details}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorId, ActorEmail = email, Action = action, EntityType = entityType,
            EntityId = entityId, Details = details, CreatedAt = created, PreviousHash = previous, Hash = hash
        });
    }
}
