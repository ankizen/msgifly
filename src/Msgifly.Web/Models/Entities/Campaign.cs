using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>A bulk WhatsApp template-message blast to a filtered or hand-picked contact segment.</summary>
public class Campaign
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ContactType RelType { get; set; } = ContactType.Lead;

    /// <summary>Meta template id (WhatsappTemplate.MetaTemplateId), not the local row PK.</summary>
    public string? TemplateId { get; set; }

    public DateTime? ScheduledSendTime { get; set; }
    public bool SendNow { get; set; }

    public string? HeaderParamsJson { get; set; }
    public string? BodyParamsJson { get; set; }
    public string? FooterParamsJson { get; set; }

    public bool PauseCampaign { get; set; }

    /// <summary>true = every contact of RelType; false = an explicit picked id list captured at creation time.</summary>
    public bool SelectAll { get; set; } = true;

    public bool IsSent { get; set; }
    public int SendingCount { get; set; }
    public string? FileName { get; set; }

    /// <summary>Saved contact filter used when SelectAll is true, e.g. {"statusId":..,"sourceId":..}.</summary>
    public string? FilterJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CampaignDetail> Details { get; set; } = new List<CampaignDetail>();
}
