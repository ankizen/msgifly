using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>Mailgun messages API — mirrors FluentSMTP's Mailgun/Handler.php: POST
/// https://api.mailgun.net/v3/{domain}/messages (or api.eu.mailgun.net for the "eu" region),
/// Basic auth "api:{key}", form-encoded body, success = HTTP 200.</summary>
public class MailgunProviderHandler : IEmailProviderHandler
{
    private const string ApiBaseUs = "https://api.mailgun.net/v3/";
    private const string ApiBaseEu = "https://api.eu.mailgun.net/v3/";

    private readonly IHttpClientFactory _httpClientFactory;

    public MailgunProviderHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmailSmtpProvider Provider => EmailSmtpProvider.Mailgun;

    public async Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(connection.ApiKey) || string.IsNullOrWhiteSpace(connection.Domain))
        {
            return EmailProviderSendResult.Fail("Mailgun API key and domain are required.");
        }

        var baseUrl = string.Equals(connection.Region, "eu", StringComparison.OrdinalIgnoreCase) ? ApiBaseEu : ApiBaseUs;
        var url = $"{baseUrl}{connection.Domain}/messages";

        var client = _httpClientFactory.CreateClient("EmailProvider");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        var authBytes = Encoding.ASCII.GetBytes($"api:{connection.ApiKey}");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        httpRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["from"] = string.IsNullOrEmpty(fromName) ? fromEmail : $"{fromName} <{fromEmail}>",
            ["to"] = request.ToEmail,
            ["subject"] = request.Subject,
            ["html"] = request.BodyHtml,
        });

        var response = await client.SendAsync(httpRequest);
        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            return EmailProviderSendResult.Ok();
        }

        return EmailProviderSendResult.Fail(ExtractMessage(body) ?? $"Mailgun API error ({(int)response.StatusCode})");
    }

    private static string? ExtractMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("message", out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
