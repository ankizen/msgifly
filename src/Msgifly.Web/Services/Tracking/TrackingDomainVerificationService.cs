using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.Automations;

namespace Msgifly.Web.Services.Tracking;

/// <summary>
/// Single source of truth for whether a workspace's tracking domain is actually live — used both by
/// the Settings page's "Check now" button and the hourly recurring job. A single HTTPS probe against
/// /r/__verify proves DNS resolution, Traefik routing this Host to this app, and a real (non
/// self-signed) Let's Encrypt cert all at once, so there's no need to separately re-implement a
/// DNS-only check.
/// </summary>
public class TrackingDomainVerificationService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TrackingDomainVerificationService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<TrackingDomainStatus> VerifyAsync(Workspace workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace.TrackingDomain))
        {
            return TrackingDomainStatus.NotConfigured;
        }

        var probeUrl = $"https://{workspace.TrackingDomain}/r/__verify";

        // Same SSRF concern as the SendWebhook automation step — an admin can type any hostname
        // here and this server will make an outbound request to it.
        if (!await WebhookUrlGuard.IsDeliverableAsync(probeUrl))
        {
            return DegradedStatus(workspace.TrackingDomainStatus);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("AutomationWebhook");
            var response = await client.GetAsync(probeUrl);
            return response.IsSuccessStatusCode
                ? TrackingDomainStatus.Active
                : DegradedStatus(workspace.TrackingDomainStatus);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return DegradedStatus(workspace.TrackingDomainStatus);
        }
    }

    /// <summary>Failed keeps a domain that used to work flagged as a regression (cert renewal
    /// failure, DNS change) — more urgent than Pending, which just means still mid-setup.</summary>
    private static TrackingDomainStatus DegradedStatus(TrackingDomainStatus current) =>
        current == TrackingDomainStatus.Active ? TrackingDomainStatus.Failed : TrackingDomainStatus.Pending;
}
