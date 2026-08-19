using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>The per-subscriber cursor through an EmailSequence's drip mails — mirrors FluentCRM's
/// fc_sequence_tracker. LastMailId/NextMailId are plain EmailSequenceMail id pointers (no FK
/// constraint — harmless orphan if a mail row is later deleted, same tradeoff as
/// TemplateButtonClick.WhatsappMessageId).</summary>
public class EmailSequenceSubscriber
{
    public int Id { get; set; }

    public int SequenceId { get; set; }
    public EmailSequence Sequence { get; set; } = null!;

    /// <summary>Contact IS the email subscriber — no separate subscriber table.</summary>
    public int SubscriberId { get; set; }
    public Contact Subscriber { get; set; } = null!;

    public EmailSequenceSubscriberStatus Status { get; set; } = EmailSequenceSubscriberStatus.Active;

    public int? LastMailId { get; set; }
    public int? NextMailId { get; set; }
    public DateTime? NextExecutionAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
