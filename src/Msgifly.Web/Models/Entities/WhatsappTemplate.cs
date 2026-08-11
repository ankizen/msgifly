using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Local cache of a Meta-approved WhatsApp Business message template.</summary>
public class WhatsappTemplate
{
    public int Id { get; set; }

    /// <summary>Meta's template id — the business key used by Campaigns/Bots to reference this template.</summary>
    public string MetaTemplateId { get; set; } = string.Empty;

    public string TemplateName { get; set; } = string.Empty;
    public string Language { get; set; } = "en_US";
    public TemplateStatus Status { get; set; } = TemplateStatus.Pending;
    public string Category { get; set; } = string.Empty;

    public string? HeaderFormat { get; set; } // TEXT | IMAGE | DOCUMENT | VIDEO
    public string? HeaderText { get; set; }
    public int HeaderParamsCount { get; set; }

    public string BodyText { get; set; } = string.Empty;
    public int BodyParamsCount { get; set; }

    public string? FooterText { get; set; }
    public int FooterParamsCount { get; set; }

    /// <summary>JSON-encoded button definitions, as returned by the Graph API.</summary>
    public string? ButtonsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
