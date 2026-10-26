using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OtpNet;
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

public sealed class TotpSecretProtector(IConfiguration configuration, IHostEnvironment environment)
{
    private byte[] GetKey()
    {
        var encoded = configuration["Authentication:MfaEncryptionKey"];
        if (string.IsNullOrWhiteSpace(encoded) && environment.IsDevelopment())
            encoded = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("LOCAL-ONLY-MFA-KEY-DO-NOT-USE-IN-PRODUCTION")));
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException("Set Authentication:MfaEncryptionKey to a base64-encoded 32-byte key.");
        var key = Convert.FromBase64String(encoded);
        if (key.Length != 32)
            throw new InvalidOperationException("Authentication:MfaEncryptionKey must decode to exactly 32 bytes.");
        return key;
    }

    public string Protect(string secret)
    {
        var key = GetKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(ciphertext).ToArray());
    }

    public string Unprotect(string protectedValue)
    {
        var packed = Convert.FromBase64String(protectedValue);
        if (packed.Length < 28) throw new CryptographicException("Invalid protected MFA secret.");
        var nonce = packed[..12];
        var tag = packed[12..28];
        var ciphertext = packed[28..];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(GetKey(), tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}

public static class TotpHelper
{
    public static string CreateSecret() => Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20));

    public static bool Verify(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || !code.All(char.IsDigit)) return false;
        var bytes = Base32Encoding.ToBytes(secret);
        return new Totp(bytes).VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
    }

    public static string SetupUri(string email, string secret) =>
        $"otpauth://totp/SIM%20Inventory:{Uri.EscapeDataString(email)}?secret={secret}&issuer=SIM%20Inventory&digits=6&period=30";
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
