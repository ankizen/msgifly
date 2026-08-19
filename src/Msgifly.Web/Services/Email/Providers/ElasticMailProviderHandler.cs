using System.Text.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>Elastic Email — mirrors FluentSMTP's ElasticMail/Handler.php: POST
/// https://api.elasticemail.com/v2/email/send?apikey=..., multipart/form-data body (FluentSMTP
/// hand-builds the multipart boundary in PHP; MultipartFormDataContent does the equivalent here).
/// Success = response JSON's "success" field is true.</summary>
public class ElasticMailProviderHandler : IEmailProviderHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ElasticMailProviderHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmailSmtpProvider Provider => EmailSmtpProvider.ElasticMail;

    public async Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            return EmailProviderSendResult.Fail("Elastic Email API key is not configured.");
        }

        var client = _httpClientFactory.CreateClient("EmailProvider");
        using var content = new MultipartFormDataContent
        {
            { new StringContent(request.Subject), "subject" },
            { new StringContent(fromEmail), "from" },
            { new StringContent(fromName ?? fromEmail), "fromName" },
            { new StringContent(request.ToEmail), "msgTo" },
            { new StringContent(request.BodyHtml), "bodyHtml" },
            { new StringContent("true"), "isTransactional" },
        };

        var url = $"https://api.elasticemail.com/v2/email/send?apikey={Uri.EscapeDataString(connection.ApiKey)}";
        var response = await client.PostAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("success", out var success) && success.GetBoolean())
            {
                return EmailProviderSendResult.Ok();
            }

            var error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "Unknown error";
            return EmailProviderSendResult.Fail($"Elastic Email API error: {error}");
        }
        catch (JsonException)
        {
            return EmailProviderSendResult.Fail($"Elastic Email API error ({(int)response.StatusCode}): {body}");
        }
    }
}
