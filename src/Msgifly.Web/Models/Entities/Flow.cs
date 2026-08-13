using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>
/// A WhatsApp Flow — a multi-screen native form rendered inside WhatsApp itself (lead capture,
/// appointment booking, surveys). This app only supports STATIC flows (fixed screens, client-side
/// navigation) — submissions arrive through the normal webhook as an nfm_reply, no separate
/// encrypted data-exchange endpoint needed. Mirrors WhatsappTemplate's shape: a local cache keyed
/// by Meta's own id, editable while Draft, immutable once Published (same lifecycle Meta enforces).
/// </summary>
public class Flow
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }

    /// <summary>Meta's flow id — null until first created on Meta's side.</summary>
    public string? MetaFlowId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>JSON string array, e.g. ["LEAD_GENERATION"] — Meta's fixed category enum.</summary>
    public string CategoriesJson { get; set; } = "[]";

    public FlowStatus Status { get; set; } = FlowStatus.Draft;

    /// <summary>The raw Flow JSON document (screens/components/navigation) — hand-authored in this
    /// app's editor rather than a visual builder; Meta's own Flow Builder in WhatsApp Manager
    /// already covers that if wanted.</summary>
    public string FlowJson { get; set; } = string.Empty;

    /// <summary>Set when a create/update/publish call to Meta fails — cleared on the next success.</summary>
    public string? SubmissionError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
