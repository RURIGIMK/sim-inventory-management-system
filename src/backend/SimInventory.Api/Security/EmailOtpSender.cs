using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SimInventory.Api.Security;

public sealed class EmailOtpSender(HttpClient httpClient, IConfiguration configuration)
{
    public async Task SendAsync(string email, string displayName, string code, CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["Email:ResendApiKey"];
        var from = configuration["Email:FromAddress"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Configure Email:ResendApiKey and Email:FromAddress.");

        var safeName = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(displayName) ? email : displayName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            from,
            to = new[] { email },
            subject = "Your SIM Inventory sign-in code",
            html = $"<div style=\"font-family:Arial,sans-serif;max-width:520px;margin:auto;color:#172033\"><h2>SIM Inventory</h2><p>Hello {safeName},</p><p>Use this code to finish signing in:</p><p style=\"font-size:30px;font-weight:700;letter-spacing:8px\">{code}</p><p>This code expires in five minutes. If you did not request it, you can ignore this email.</p></div>",
            text = $"Hello {displayName}, your SIM Inventory sign-in code is {code}. It expires in five minutes. If you did not request it, ignore this email."
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The email provider rejected the OTP email request.");
    }
}
