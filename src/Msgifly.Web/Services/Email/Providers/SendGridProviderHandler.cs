using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>SendGrid mail API — mirrors FluentSMTP's SendGrid/Handler.php: POST
/// https://api.sendgrid.com/v3/mail/send, Bearer auth, success = HTTP 202.</summary>
public class SendGridProviderHandler : IEmailProviderHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SendGridProviderHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmailSmtpProvider Provider => EmailSmtpProvider.SendGrid;

    public async Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            return EmailProviderSendResult.Fail("SendGrid API key is not configured.");
        }

        var client = _httpClientFactory.CreateClient("EmailProvider");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            from = new { email = fromEmail, name = fromName ?? fromEmail },
            personalizations = new[] { new { to = new[] { new { email = request.ToEmail } } } },
            subject = request.Subject,
            content = new[] { new { type = "text/html", value = request.BodyHtml } },
        });

        var response = await client.SendAsync(httpRequest);
        if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            return EmailProviderSendResult.Ok();
        }

        var body = await response.Content.ReadAsStringAsync();
        return EmailProviderSendResult.Fail(ExtractFirstError(body) ?? $"SendGrid API error ({(int)response.StatusCode})");
    }

    private static string? ExtractFirstError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            {
                return errors[0].TryGetProperty("message", out var message) ? message.GetString() : null;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
