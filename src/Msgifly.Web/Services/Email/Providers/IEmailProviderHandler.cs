using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

public record EmailProviderSendResult(bool Success, string? ErrorMessage = null)
{
    public static EmailProviderSendResult Ok() => new(true);
    public static EmailProviderSendResult Fail(string message) => new(false, message);
}

/// <summary>One handler per EmailSmtpProvider — mirrors FluentSMTP's Providers/{Name}/Handler.php
/// split, each talking to that provider's real send API rather than routing everything through a
/// generic SMTP relay.</summary>
public interface IEmailProviderHandler
{
    EmailSmtpProvider Provider { get; }

    Task<EmailProviderSendResult> SendAsync(EmailSmtpConnection connection, string fromEmail, string? fromName, EmailSendRequest request);
}
