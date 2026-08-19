using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>Cloudflare Email Sending — mirrors FluentSMTP's Cloudflare/Handler.php: POST
/// https://api.cloudflare.com/client/v4/accounts/{account_id}/email/sending/send, Bearer auth,
/// success = HTTP 200 and the response body's "success" field is true.</summary>
public class CloudflareProviderHandler : IEmailProviderHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public CloudflareProviderHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmailSmtpProvider Provider => EmailSmtpProvider.Cloudflare;

    public async Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(connection.ApiKey) || string.IsNullOrWhiteSpace(connection.AccountId))
        {
            return EmailProviderSendResult.Fail("Cloudflare API token and Account ID are required.");
        }

        var url = $"https://api.cloudflare.com/client/v4/accounts/{Uri.EscapeDataString(connection.AccountId)}/email/sending/send";
        var client = _httpClientFactory.CreateClient("EmailProvider");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            from = fromEmail,
            to = new[] { request.ToEmail },
            subject = request.Subject,
            html = request.BodyHtml,
        });

        var response = await client.SendAsync(httpRequest);
        var body = await response.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (response.StatusCode == System.Net.HttpStatusCode.OK && doc.RootElement.TryGetProperty("success", out var success) && success.GetBoolean())
            {
                return EmailProviderSendResult.Ok();
            }

            var error = doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0
                ? errors[0].TryGetProperty("message", out var m) ? m.GetString() : null
                : null;
            return EmailProviderSendResult.Fail(error ?? $"Cloudflare API error ({(int)response.StatusCode})");
        }
        catch (JsonException)
        {
            return EmailProviderSendResult.Fail($"Cloudflare API error ({(int)response.StatusCode}): {body}");
        }
    }
}
