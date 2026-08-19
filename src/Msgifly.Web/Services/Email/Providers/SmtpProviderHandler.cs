using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>Generic SMTP relay via MailKit (SmtpClient/System.Net.Mail is Microsoft-obsolete) —
/// the original, provider-agnostic sending path, still the default/fallback provider.</summary>
public class SmtpProviderHandler : IEmailProviderHandler
{
    public EmailSmtpProvider Provider => EmailSmtpProvider.Smtp;

    public async Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(connection.Host))
        {
            return EmailProviderSendResult.Fail("SMTP host is not configured.");
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName ?? fromEmail, fromEmail));
            message.To.Add(MailboxAddress.Parse(request.ToEmail));
            message.Subject = request.Subject;
            message.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = request.BodyHtml };

            using var client = new SmtpClient();
            await client.ConnectAsync(connection.Host, connection.Port ?? 587, connection.EnableSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None);
            if (!string.IsNullOrEmpty(connection.Username))
            {
                await client.AuthenticateAsync(connection.Username, connection.Password ?? string.Empty);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            return EmailProviderSendResult.Ok();
        }
        catch (Exception ex)
        {
            return EmailProviderSendResult.Fail(ex.Message);
        }
    }
}
