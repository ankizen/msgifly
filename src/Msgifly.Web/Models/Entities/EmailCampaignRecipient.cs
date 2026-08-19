using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Per-recipient send record for an EmailCampaign — materialized at campaign-save time,
/// not lazily in the dispatch cron (mirrors CampaignDetail's exact precedent).</summary>
public class EmailCampaignRecipient
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public EmailCampaign Campaign { get; set; } = null!;

    public int SubscriberId { get; set; }
    public EmailSubscriber Subscriber { get; set; } = null!;

    public EmailCampaignRecipientStatus Status { get; set; } = EmailCampaignRecipientStatus.Pending;

    public int? EmailLogId { get; set; }
    public EmailLog? EmailLog { get; set; }

    /// <summary>Unique lookup key for the /e/o, /e/c, /e/u tracking endpoints — a fresh Guid.NewGuid("N") per row at insert time.</summary>
    public string TrackingToken { get; set; } = string.Empty;

    public bool IsOpened { get; set; }
    public DateTime? OpenedAt { get; set; }

    public bool IsClicked { get; set; }
    public int ClickCount { get; set; }
    public DateTime? ClickedAt { get; set; }

    public bool IsUnsubscribed { get; set; }
    public DateTime? UnsubscribedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
