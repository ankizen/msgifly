using Hangfire;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Jobs;

/// <summary>
/// Runs every minute (registered as a Hangfire recurring job in Program.cs) — the equivalent of
/// the original's `campaigns:process-scheduled` cron command (master doc §5.2). Finds due,
/// unpaused campaigns and enqueues one CampaignMessageJob per pending recipient.
/// </summary>
public class CampaignDispatchJob
{
    private readonly ApplicationDbContext _db;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<CampaignDispatchJob> _logger;

    public CampaignDispatchJob(ApplicationDbContext db, IBackgroundJobClient backgroundJobClient, ILogger<CampaignDispatchJob> logger)
    {
        _db = db;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public async Task ProcessScheduledCampaignsAsync()
    {
        // Runs once for the whole app (no per-workspace HttpContext to scope it), so this
        // deliberately sweeps every workspace's due campaigns in one pass — IgnoreQueryFilters()
        // on both queries here, not just the outer one. Each enqueued CampaignMessageJob then
        // bootstraps its own workspace context individually when it actually runs.
        var now = DateTime.UtcNow;
        var dueCampaigns = await _db.Campaigns.IgnoreQueryFilters()
            .Where(c => !c.IsSent && !c.PauseCampaign)
            .Where(c => c.SendNow || (c.ScheduledSendTime != null && c.ScheduledSendTime <= now))
            .ToListAsync();

        foreach (var campaign in dueCampaigns)
        {
            var pendingDetailIds = await _db.CampaignDetails.IgnoreQueryFilters()
                .Where(d => d.CampaignId == campaign.Id && d.Status == CampaignDetailStatus.Pending)
                .Select(d => d.Id)
                .ToListAsync();

            foreach (var detailId in pendingDetailIds)
            {
                _backgroundJobClient.Enqueue<CampaignMessageJob>(job => job.SendMessageAsync(detailId));
            }

            campaign.IsSent = true;
            _logger.LogInformation("Queued {Count} message(s) for campaign {CampaignId} ({Name})",
                pendingDetailIds.Count, campaign.Id, campaign.Name);
        }

        if (dueCampaigns.Count > 0)
        {
            await _db.SaveChangesAsync();
        }
    }
}
