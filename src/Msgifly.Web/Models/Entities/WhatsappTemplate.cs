using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Local cache of a Meta-approved WhatsApp Business message template.</summary>
public class WhatsappTemplate
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }

    /// <summary>Meta's template id — the business key used by Campaigns/Bots to reference this template. Null until a locally-created DRAFT is first submitted.</summary>
    public string? MetaTemplateId { get; set; }

    public string TemplateName { get; set; } = string.Empty;
    public string Language { get; set; } = "en_US";
    public TemplateStatus Status { get; set; } = TemplateStatus.Pending;
    public string Category { get; set; } = string.Empty;

    public string? HeaderFormat { get; set; } // TEXT | IMAGE | DOCUMENT | VIDEO
    public string? HeaderText { get; set; }
    public string? HeaderMediaUrl { get; set; }
    public int HeaderParamsCount { get; set; }

    public string BodyText { get; set; } = string.Empty;
    public int BodyParamsCount { get; set; }

    public string? FooterText { get; set; }
    public int FooterParamsCount { get; set; }

    /// <summary>JSON-encoded button definitions, as returned by the Graph API (or authored locally before submission).</summary>
    public string? ButtonsJson { get; set; }

    /// <summary>JSON {"header":[...],"body":[...]} — sample values Meta requires for human review; kept so "Edit" can resubmit without asking again.</summary>
    public string? SampleValuesJson { get; set; }

    /// <summary>Meta's rejection reason, if Status is Rejected (pulled from template sync).</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Set when a create/edit submission to Meta fails — cleared on the next successful submission.</summary>
    public string? SubmissionError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
