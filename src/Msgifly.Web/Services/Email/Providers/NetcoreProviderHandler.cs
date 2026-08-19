using System.Net.Http.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>Netcore Email API (formerly Pepipost) — mirrors FluentSMTP's PepiPost/Handler.php:
/// POST https://api.pepipost.com/v5/mail/send, raw api key in the "api_key" header, success =
/// HTTP 202.</summary>
public class NetcoreProviderHandler : IEmailProviderHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public NetcoreProviderHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmailSmtpProvider Provider => EmailSmtpProvider.Netcore;

    public async Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            return EmailProviderSendResult.Fail("Netcore API key is not configured.");
        }

        var client = _httpClientFactory.CreateClient("EmailProvider");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.pepipost.com/v5/mail/send");
        httpRequest.Headers.Add("api_key", connection.ApiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            from = new { name = fromName ?? fromEmail, email = fromEmail },
            personalizations = new[] { new { to = new[] { new { email = request.ToEmail } } } },
            subject = request.Subject,
            content = new[] { new { type = "html", value = request.BodyHtml } },
        });

        var response = await client.SendAsync(httpRequest);
        if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            return EmailProviderSendResult.Ok();
        }

        var body = await response.Content.ReadAsStringAsync();
        return EmailProviderSendResult.Fail($"Netcore API error ({(int)response.StatusCode}): {body}");
    }
}
