using Amazon;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>Amazon SES — uses the official AWS SDK (SigV4 request signing handled by the SDK,
/// not hand-rolled) rather than FluentSMTP's own bundled SimpleEmailServiceRequest signer.</summary>
public class AmazonSesProviderHandler : IEmailProviderHandler
{
    public EmailSmtpProvider Provider => EmailSmtpProvider.AmazonSes;

    public async Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(connection.AccessKey) || string.IsNullOrWhiteSpace(connection.SecretKey))
        {
            return EmailProviderSendResult.Fail("Amazon SES access key and secret key are required.");
        }

        var region = string.IsNullOrWhiteSpace(connection.Region) ? "us-east-1" : connection.Region;

        try
        {
            using var client = new AmazonSimpleEmailServiceClient(connection.AccessKey, connection.SecretKey, RegionEndpoint.GetBySystemName(region));

            var sesRequest = new SendEmailRequest
            {
                Source = string.IsNullOrEmpty(fromName) ? fromEmail : $"{fromName} <{fromEmail}>",
                Destination = new Destination { ToAddresses = [request.ToEmail] },
                Message = new Message
                {
                    Subject = new Content(request.Subject),
                    Body = new Body { Html = new Content { Charset = "UTF-8", Data = request.BodyHtml } },
                },
            };

            await client.SendEmailAsync(sesRequest);
            return EmailProviderSendResult.Ok();
        }
        catch (Exception ex)
        {
            return EmailProviderSendResult.Fail(ex.Message);
        }
    }
}
