using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>
/// One tenant/business — its own Contacts, Templates, Campaigns, Chat, Bots and Automations all
/// hang off this via WorkspaceId (see ApplicationDbContext's global query filters). The Meta
/// Developer App itself (FacebookAppId/Secret, webhook verify token) stays global — one App,
/// shared across every Workspace — but the WhatsApp Business Account connected under that App is
/// per-Workspace, so its connection state lives directly here rather than in the old singleton
/// AppSetting "WhatsAppSettings" group.
/// </summary>
public class Workspace
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // WhatsApp Business Account connection (per-workspace).
    public bool IsAccountConnected { get; set; }
    public string? BusinessAccountId { get; set; }
    public string? AccessToken { get; set; }
    public string? DefaultPhoneNumberId { get; set; }
    public string? DefaultPhoneNumber { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public DateTime? LastHealthCheckAt { get; set; }
    public string? HealthStatusJson { get; set; }

    // How this WABA was connected — manual token entry (Phase 1) vs Embedded Signup (Phase 2).
    public string? ConnectionMethod { get; set; }

    // Facebook Page linked for Lead Ads sync (Phase 3) — separate from the WABA connection above.
    public string? FacebookPageId { get; set; }
    public string? FacebookPageName { get; set; }
    public string? FacebookPageAccessToken { get; set; }

    // Per-business domain (e.g. link.salonsteps.com) that WhatsApp template URL buttons get routed
    // through for real per-recipient click tracking — WhatsApp gives no signal at all for a tapped
    // URL button otherwise. Kept per-workspace rather than a shared app domain so the link a lead
    // sees carries that business's own brand, never ours.
    public string? TrackingDomain { get; set; }
    public TrackingDomainStatus TrackingDomainStatus { get; set; } = TrackingDomainStatus.NotConfigured;
    public DateTime? TrackingDomainCheckedAt { get; set; }

    // Bot/lead behavior that used to live in the global "WhatsMarkSettings" AppSetting group —
    // moved here since a default lead status/source and stop-bot keywords are inherently
    // per-business, not global.
    public bool AutoCreateLeadOnInboundMessage { get; set; } = true;
    public int? DefaultLeadStatusId { get; set; }
    public int? DefaultLeadSourceId { get; set; }
    public string StopBotKeywords { get; set; } = "stop,unsubscribe";
    public int RestartBotsAfterHours { get; set; } = 24;
}
