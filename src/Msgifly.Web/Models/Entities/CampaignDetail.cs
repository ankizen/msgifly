using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Per-recipient send record for a Campaign.</summary>
public class CampaignDetail
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public string? HeaderMessage { get; set; }
    public string? BodyMessage { get; set; }
    public string? FooterMessage { get; set; }

    public CampaignDetailStatus Status { get; set; } = CampaignDetailStatus.Pending;
    public string? ResponseMessage { get; set; }

    /// <summary>WhatsApp message id (wamid...) returned by Meta — correlates delivery-status webhooks.</summary>
    public string? WhatsappMessageId { get; set; }

    public MessageDeliveryStatus? DeliveryStatus { get; set; }

    // Per-stage delivery timestamps — DeliveryStatus alone only reflects the *latest* stage
    // reached, so these are what actually let a report show sent→delivered→read timing/funnel.
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? FailedAt { get; set; }

    /// <summary>True if this recipient tapped the template's quick-reply button.</summary>
    public bool Clicked { get; set; }
    public string? ClickedButtonText { get; set; }

    /// <summary>Set when the recipient sends any inbound message (button tap or free text) that
    /// replies to this specific send — the "engaged" signal a follow-up re-send segments on.</summary>
    public DateTime? RepliedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
