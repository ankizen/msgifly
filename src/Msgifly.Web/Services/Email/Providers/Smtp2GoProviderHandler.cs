using System.Net.Http.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>SMTP2GO — mirrors FluentSMTP's Smtp2Go/Handler.php: POST
/// https://api.smtp2go.com/v3/email/send, "X-Smtp2go-Api-Key" header, success = HTTP 200.</summary>
public class Smtp2GoProviderHandler : IEmailProviderHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public Smtp2GoProviderHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmailSmtpProvider Provider => EmailSmtpProvider.Smtp2Go;

    public async Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            return EmailProviderSendResult.Fail("SMTP2GO API key is not configured.");
        }

        var client = _httpClientFactory.CreateClient("EmailProvider");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.smtp2go.com/v3/email/send");
        httpRequest.Headers.Add("X-Smtp2go-Api-Key", connection.ApiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            sender = string.IsNullOrEmpty(fromName) ? fromEmail : $"{fromName} <{fromEmail}>",
            to = new[] { request.ToEmail },
            subject = request.Subject,
            html_body = request.BodyHtml,
        });

        var response = await client.SendAsync(httpRequest);
        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            return EmailProviderSendResult.Ok();
        }

        var body = await response.Content.ReadAsStringAsync();
        return EmailProviderSendResult.Fail($"SMTP2GO API error ({(int)response.StatusCode}): {body}");
    }
}
