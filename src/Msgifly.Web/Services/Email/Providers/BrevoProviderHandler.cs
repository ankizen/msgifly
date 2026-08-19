using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>Brevo (formerly Sendinblue) transactional email API — mirrors FluentSMTP's
/// SendInBlue/Handler.php exactly: POST https://api.brevo.com/v3/smtp/email, Api-Key header,
/// success = HTTP 201.</summary>
public class BrevoProviderHandler : IEmailProviderHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly IHttpClientFactory _httpClientFactory;

    public BrevoProviderHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmailSmtpProvider Provider => EmailSmtpProvider.Brevo;

    public async Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            return EmailProviderSendResult.Fail("Brevo API key is not configured.");
        }

        var client = _httpClientFactory.CreateClient("EmailProvider");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        httpRequest.Headers.Add("Api-Key", connection.ApiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            sender = new { name = fromName ?? fromEmail, email = fromEmail },
            to = new[] { new { email = request.ToEmail } },
            subject = request.Subject,
            htmlContent = request.BodyHtml,
        }, options: JsonOptions);

        var response = await client.SendAsync(httpRequest);
        if (response.StatusCode == System.Net.HttpStatusCode.Created)
        {
            return EmailProviderSendResult.Ok();
        }

        var body = await response.Content.ReadAsStringAsync();
        return EmailProviderSendResult.Fail(ExtractMessage(body, "message") ?? $"Brevo API error ({(int)response.StatusCode})");
    }

    private static string? ExtractMessage(string json, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
