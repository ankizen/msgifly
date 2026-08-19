using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.Email;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Jobs;

/// <summary>
/// Sends one campaign email to one recipient — enqueued (staggered) per-recipient by
/// EmailCampaignDispatchJob, mirroring CampaignMessageJob exactly. IEmailSender never throws on a
/// delivery failure (it returns EmailSendResult), so a bad address is recorded as Failed without
/// triggering Hangfire's automatic retry; genuine unhandled exceptions (a DB blip) still propagate
/// and get Hangfire's default retry as a safety net.
/// </summary>
public class EmailCampaignMessageJob
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly EmailMergeTagRenderer _mergeTagRenderer;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly ILogger<EmailCampaignMessageJob> _logger;

    public EmailCampaignMessageJob(
        ApplicationDbContext db,
        IEmailSender emailSender,
        EmailMergeTagRenderer mergeTagRenderer,
        ICurrentWorkspaceAccessor workspaceAccessor,
        ILogger<EmailCampaignMessageJob> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _mergeTagRenderer = mergeTagRenderer;
        _workspaceAccessor = workspaceAccessor;
        _logger = logger;
    }

    public async Task SendAsync(int recipientId)
    {
        // No HttpContext here — bootstrap from the recipient's own Campaign before any filtered
        // query runs, same pattern as CampaignMessageJob/AutomationEngine.ResumeWaitAsync.
        var workspaceId = await _db.EmailCampaignRecipients.IgnoreQueryFilters()
            .Where(r => r.Id == recipientId)
            .Select(r => (int?)r.Campaign.WorkspaceId)
            .FirstOrDefaultAsync();
        if (workspaceId is null)
        {
            _logger.LogWarning("EmailCampaignRecipient {Id} no longer exists; skipping.", recipientId);
            return;
        }

        _workspaceAccessor.WorkspaceId = workspaceId;

        var recipient = await _db.EmailCampaignRecipients
            .Include(r => r.Campaign)
            .Include(r => r.Subscriber)
            .FirstOrDefaultAsync(r => r.Id == recipientId);
        if (recipient is null || recipient.Subscriber is null)
        {
            return;
        }

        if (recipient.Status != EmailCampaignRecipientStatus.Pending)
        {
            return;
        }

        if (recipient.Campaign.Status == EmailCampaignStatus.Paused)
        {
            return; // left Pending — picked up again once the campaign is resumed
        }

        // EmailAudienceResolver already filters to Contacts with a real email at materialization
        // time, so this should never be blank in practice — but Email is optional on Contact now
        // (unlike the old dedicated EmailSubscriber table), so guard defensively rather than crash.
        if (string.IsNullOrWhiteSpace(recipient.Subscriber.Email))
        {
            recipient.Status = EmailCampaignRecipientStatus.Failed;
            recipient.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return;
        }

        var subject = _mergeTagRenderer.Render(recipient.Campaign.Subject, recipient.Subscriber, recipient.TrackingToken);
        var body = _mergeTagRenderer.Render(recipient.Campaign.BodyHtml, recipient.Subscriber, recipient.TrackingToken);

        var result = await _emailSender.SendAsync(new EmailSendRequest(
            recipient.Subscriber.Email,
            subject,
            body,
            recipient.Campaign.FromEmail,
            recipient.Campaign.FromName,
            $"Campaign:{recipient.CampaignId}"));

        recipient.Status = result.Success ? EmailCampaignRecipientStatus.Sent : EmailCampaignRecipientStatus.Failed;
        recipient.EmailLogId = result.EmailLogId;
        recipient.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }
}
