using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>One shared "every email this app ever tried to send" audit table — written by
/// IEmailSender for every campaign/automation/sequence/transactional send alike.</summary>
public class EmailLog
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }

    public string ToEmail { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    public EmailLogStatus Status { get; set; } = EmailLogStatus.Pending;
    public string? ResponseMessage { get; set; }
    public int RetryCount { get; set; }

    /// <summary>Free text, not FK — e.g. "Campaign:123" / "Automation:45" / "Sequence:12" /
    /// "Transactional" — keeps IEmailSender decoupled from any one caller's table.</summary>
    public string Source { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}
