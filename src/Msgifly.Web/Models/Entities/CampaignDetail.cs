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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
