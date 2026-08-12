namespace Msgifly.Web.Models.Entities;

/// <summary>
/// One row per Lead Ads form Meta reports for the workspace's connected Page — a Page can run
/// many forms over time and not all of them stay active, so syncing is opt-in per form rather
/// than blanket page-wide. Discovered/refreshed automatically (LeadAdsSyncJob upserts this list
/// every run, and LeadAdsController does the same right after connecting a Page so the list
/// isn't empty until the next scheduled run) — newly-discovered forms default to IsEnabled=false,
/// the admin turns on only the ones they actually want synced.
/// </summary>
public class LeadAdsForm
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string FormId { get; set; } = string.Empty;
    public string FormName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
