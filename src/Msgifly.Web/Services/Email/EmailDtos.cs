namespace Msgifly.Web.Services.Email;

public record EmailSendResult(bool Success, int? EmailLogId = null, string? ErrorMessage = null)
{
    public static EmailSendResult Ok(int emailLogId) => new(true, emailLogId);
    public static EmailSendResult Fail(string message, int? emailLogId = null) => new(false, emailLogId, message);
}

/// <summary>One email to send. FromEmail/FromName are optional overrides — when omitted,
/// EmailSenderService falls back to the resolved EmailSmtpConnection's own From identity.</summary>
public record EmailSendRequest(string ToEmail, string Subject, string BodyHtml, string? FromEmail = null, string? FromName = null, string Source = "Transactional");
