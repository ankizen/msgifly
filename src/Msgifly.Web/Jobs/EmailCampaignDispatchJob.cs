using Hangfire;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Jobs;

/// <summary>
/// Runs every minute — mirrors CampaignDispatchJob's exact two-phase design. Recipients are
/// already materialized at campaign-save time (EmailCampaignsController.Save), so this only
/// sweeps due/Scheduled campaigns and staggers one Hangfire job per pending recipient. Also flips
/// any Sending campaign with zero remaining Pending recipients to Sent, so no second recurring
/// job is needed for that.
/// </summary>
public class EmailCampaignDispatchJob
{
    private readonly ApplicationDbContext _db;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<EmailCampaignDispatchJob> _logger;

    public EmailCampaignDispatchJob(ApplicationDbContext db, IBackgroundJobClient backgroundJobClient, ILogger<EmailCampaignDispatchJob> logger)
    {
        _db = db;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public async Task ProcessScheduledCampaignsAsync()
    {
        var now = DateTime.UtcNow;
        var dueCampaigns = await _db.EmailCampaigns.IgnoreQueryFilters()
            .Where(c => c.Status == EmailCampaignStatus.Scheduled)
            .Where(c => c.SendNow || (c.ScheduledAt != null && c.ScheduledAt <= now))
            .ToListAsync();

        foreach (var campaign in dueCampaigns)
        {
            // Only used to pace the stagger between sends — the real per-send connection
            // resolution (exact FromEmail match, else default) happens fresh inside
            // EmailSenderService when each message actually goes out.
            var connection = await _db.EmailSmtpConnections.IgnoreQueryFilters()
                .Where(c => c.WorkspaceId == campaign.WorkspaceId && c.IsActive)
                .OrderByDescending(c => c.IsDefault)
                .FirstOrDefaultAsync();
            var perSendDelaySeconds = connection is { MaxSendsPerMinute: > 0 } ? 60.0 / connection.MaxSendsPerMinute : 2.0;

            var pendingIds = await _db.EmailCampaignRecipients.IgnoreQueryFilters()
                .Where(r => r.CampaignId == campaign.Id && r.Status == EmailCampaignRecipientStatus.Pending)
                .Select(r => r.Id)
                .ToListAsync();

            for (var i = 0; i < pendingIds.Count; i++)
            {
                var delay = TimeSpan.FromSeconds(i * perSendDelaySeconds);
                _backgroundJobClient.Schedule<EmailCampaignMessageJob>(job => job.SendAsync(pendingIds[i]), delay);
            }

            campaign.Status = EmailCampaignStatus.Sending;
            _logger.LogInformation("Queued {Count} email(s) for campaign {CampaignId} ({Name})", pendingIds.Count, campaign.Id, campaign.Name);
        }

        var sendingCampaigns = await _db.EmailCampaigns.IgnoreQueryFilters()
            .Where(c => c.Status == EmailCampaignStatus.Sending)
            .ToListAsync();
        foreach (var campaign in sendingCampaigns)
        {
            var hasPending = await _db.EmailCampaignRecipients.IgnoreQueryFilters()
                .AnyAsync(r => r.CampaignId == campaign.Id && r.Status == EmailCampaignRecipientStatus.Pending);
            if (!hasPending)
            {
                campaign.Status = EmailCampaignStatus.Sent;
            }
        }

        if (dueCampaigns.Count > 0 || sendingCampaigns.Count > 0)
        {
            await _db.SaveChangesAsync();
        }
    }
}
