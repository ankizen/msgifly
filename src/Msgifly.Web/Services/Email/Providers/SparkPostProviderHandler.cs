using System.Net.Http.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>SparkPost transmissions API — mirrors FluentSMTP's SparkPost/Handler.php: POST
/// https://api.sparkpost.com/api/v1/transmissions, raw api key in the Authorization header (no
/// "Bearer" prefix), success = any HTTP status under 300.</summary>
public class SparkPostProviderHandler : IEmailProviderHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SparkPostProviderHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmailSmtpProvider Provider => EmailSmtpProvider.SparkPost;

    public async Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            return EmailProviderSendResult.Fail("SparkPost API key is not configured.");
        }

        var client = _httpClientFactory.CreateClient("EmailProvider");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.sparkpost.com/api/v1/transmissions");
        httpRequest.Headers.Add("Authorization", connection.ApiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            options = new { sandbox = false },
            content = new
            {
                from = string.IsNullOrEmpty(fromName) ? fromEmail : $"{fromName} <{fromEmail}>",
                subject = request.Subject,
                html = request.BodyHtml,
            },
            recipients = new[] { new { address = new { email = request.ToEmail } } },
        });

        var response = await client.SendAsync(httpRequest);
        if ((int)response.StatusCode < 300)
        {
            return EmailProviderSendResult.Ok();
        }

        var body = await response.Content.ReadAsStringAsync();
        return EmailProviderSendResult.Fail($"SparkPost API error ({(int)response.StatusCode}): {body}");
    }
}
