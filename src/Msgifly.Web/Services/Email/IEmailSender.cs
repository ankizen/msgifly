namespace Msgifly.Web.Services.Email;

/// <summary>Mirrors IWhatsAppService's result-wrapper pattern — non-throwing, callers branch on
/// Success rather than catching. Every call writes exactly one EmailLog row (Sent or Failed)
/// regardless of caller, so campaigns/automations/sequences/transactional sends all get one
/// shared audit trail for free.</summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailSendRequest request);
}
