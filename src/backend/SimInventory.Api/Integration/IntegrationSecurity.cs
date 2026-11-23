using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SimInventory.Api.Integration;

/// <summary>
/// Machine-to-machine access for the scheduled ML pipeline and scheduled HR sync.
/// These callers have no Google account, so they present a long random token instead.
/// The token grants only the /api/integrations/* routes it is attached to.
/// </summary>
public sealed class IntegrationTokenFilter(IConfiguration configuration) : IEndpointFilter
{
    public const string HeaderName = "X-Integration-Token";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = configuration["Integration:Token"];
        if (string.IsNullOrWhiteSpace(expected) || Encoding.UTF8.GetByteCount(expected) < 32)
            return Results.Problem("Integration access is not configured on this server (Integration:Token).", statusCode: 503);
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (!FixedTimeEquals(provided, expected))
            return Results.Unauthorized();
        return await next(context);
    }

    public static bool FixedTimeEquals(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided ?? string.Empty)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    /// <summary>Audit actor used for events written by an integration caller.</summary>
    public static ClaimsPrincipal Actor(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Email, $"integration:{name}")], "integration"));
}

/// <summary>
/// Verifies pushes from the HR system: header X-Hr-Timestamp (unix seconds) and
/// X-Hr-Signature: sha256=HEX(HMAC_SHA256(secret, timestamp + "." + rawBody)).
/// The timestamp window blocks replay of a captured request.
/// </summary>
public static class HrWebhookSignature
{
    public const string TimestampHeader = "X-Hr-Timestamp";
    public const string SignatureHeader = "X-Hr-Signature";
    public static readonly TimeSpan AllowedSkew = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "." + body))).ToLowerInvariant();
    }

    public static bool IsValid(string secret, string? timestamp, string? signature, string body, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signature)) return false;
        if (!long.TryParse(timestamp, out var seconds)) return false;
        var sent = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if ((now - sent).Duration() > AllowedSkew) return false;
        var expected = Sign(secret, timestamp, body);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
    }
}

/// <summary>
/// Pulls the full directory from the HR system. Configure Hr:DirectoryUrl (HTTPS) and, if the HR API
/// needs one, Hr:ApiKey with Hr:ApiKeyHeader (default "X-Api-Key"). The endpoint must return the
/// <see cref="HrDirectoryPayload"/> JSON contract; it is treated as a snapshot.
/// </summary>
public sealed class HrDirectoryClient(HttpClient http, IConfiguration configuration)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Hr:DirectoryUrl"]);

    public async Task<HrDirectoryPayload> FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        var url = configuration["Hr:DirectoryUrl"];
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("Hr:DirectoryUrl is not configured.");
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hr:DirectoryUrl must use HTTPS.");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var apiKey = configuration["Hr:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation(configuration["Hr:ApiKeyHeader"] ?? "X-Api-Key", apiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HR directory returned HTTP {(int)response.StatusCode}.");
        var payload = await JsonSerializer.DeserializeAsync<HrDirectoryPayload>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken)
            ?? throw new InvalidOperationException("HR directory returned an empty response.");
        return payload with { Mode = "snapshot" };
    }
}
