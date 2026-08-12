namespace Msgifly.Web.Models.Entities;

/// <summary>
/// One row per Meta Lead Ads lead the sync job has already pulled in — an append-only dedup
/// ledger, not business data itself (the actual Contact is what matters going forward). Exists
/// only so LeadAdsSyncJob's periodic poll of "the last N leads per form" can tell which ones are
/// already imported without re-fetching or re-parsing anything from Meta.
/// </summary>
public class LeadAdsImport
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string MetaLeadId { get; set; } = string.Empty;
    public string FormId { get; set; } = string.Empty;
    public int? ContactId { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
