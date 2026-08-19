using System.Net.Http.Json;
using System.Text.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>Postmark email API — mirrors FluentSMTP's Postmark/Handler.php: POST
/// https://api.postmarkapp.com/email, X-Postmark-Server-Token header, success = HTTP 200.</summary>
public class PostmarkProviderHandler : IEmailProviderHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public PostmarkProviderHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmailSmtpProvider Provider => EmailSmtpProvider.Postmark;

    public async Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            return EmailProviderSendResult.Fail("Postmark server API token is not configured.");
        }

        var client = _httpClientFactory.CreateClient("EmailProvider");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.postmarkapp.com/email");
        httpRequest.Headers.Add("X-Postmark-Server-Token", connection.ApiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            From = string.IsNullOrEmpty(fromName) ? fromEmail : $"{fromName} <{fromEmail}>",
            To = request.ToEmail,
            Subject = request.Subject,
            HtmlBody = request.BodyHtml,
            MessageStream = "outbound",
        });

        var response = await client.SendAsync(httpRequest);
        if (response.IsSuccessStatusCode)
        {
            return EmailProviderSendResult.Ok();
        }

        var body = await response.Content.ReadAsStringAsync();
        return EmailProviderSendResult.Fail(ExtractMessage(body) ?? $"Postmark API error ({(int)response.StatusCode})");
    }

    private static string? ExtractMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("Message", out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
