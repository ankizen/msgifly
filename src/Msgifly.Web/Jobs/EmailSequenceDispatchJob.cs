using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.Email;
using Msgifly.Web.Services.EmailSequences;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Jobs;

/// <summary>
/// Runs every minute — a single recurring sweep is enough since each EmailSequenceSubscriber
/// carries its own NextExecutionAt, unlike the campaign path there's no separate
/// materialize-then-drain split or per-recipient Hangfire scheduling needed: strictly linear, no
/// branching, matches FluentCRM's own tracker-plus-cron design for Sequences.
/// </summary>
public class EmailSequenceDispatchJob
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly EmailMergeTagRenderer _mergeTagRenderer;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly ILogger<EmailSequenceDispatchJob> _logger;

    public EmailSequenceDispatchJob(
        ApplicationDbContext db,
        IEmailSender emailSender,
        EmailMergeTagRenderer mergeTagRenderer,
        ICurrentWorkspaceAccessor workspaceAccessor,
        ILogger<EmailSequenceDispatchJob> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _mergeTagRenderer = mergeTagRenderer;
        _workspaceAccessor = workspaceAccessor;
        _logger = logger;
    }

    public async Task ProcessDueAsync()
    {
        var now = DateTime.UtcNow;
        var dueTrackerIds = await _db.EmailSequenceSubscribers.IgnoreQueryFilters()
            .Where(s => s.Status == EmailSequenceSubscriberStatus.Active && s.NextExecutionAt != null && s.NextExecutionAt <= now)
            .Select(s => s.Id)
            .ToListAsync();

        foreach (var trackerId in dueTrackerIds)
        {
            try
            {
                await ProcessOneAsync(trackerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EmailSequenceSubscriber {Id} failed to process", trackerId);
            }
        }
    }

    private async Task ProcessOneAsync(int trackerId)
    {
        // No HttpContext here — bootstrap from the tracker's own Sequence first, same pattern as
        // EmailCampaignMessageJob/EmailAutomationEngine.ResumeWaitAsync.
        var workspaceId = await _db.EmailSequenceSubscribers.IgnoreQueryFilters()
            .Where(t => t.Id == trackerId)
            .Select(t => (int?)t.Sequence.WorkspaceId)
            .FirstOrDefaultAsync();
        if (workspaceId is null)
        {
            return;
        }

        _workspaceAccessor.WorkspaceId = workspaceId;

        var tracker = await _db.EmailSequenceSubscribers
            .Include(t => t.Subscriber)
            .Include(t => t.Sequence)
            .FirstOrDefaultAsync(t => t.Id == trackerId);
        if (tracker is null || tracker.Status != EmailSequenceSubscriberStatus.Active)
        {
            return;
        }

        if (tracker.Subscriber.Status is EmailSubscriberStatus.Unsubscribed or EmailSubscriberStatus.Bounced or EmailSubscriberStatus.Complained)
        {
            tracker.Status = EmailSequenceSubscriberStatus.Cancelled;
            tracker.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return;
        }

        var mail = tracker.NextMailId is null
            ? null
            : await _db.EmailSequenceMails.FirstOrDefaultAsync(m => m.Id == tracker.NextMailId);
        if (mail is null)
        {
            tracker.Status = EmailSequenceSubscriberStatus.Completed;
            tracker.NextExecutionAt = null;
            tracker.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return;
        }

        var subject = _mergeTagRenderer.Render(mail.Subject, tracker.Subscriber);
        var body = _mergeTagRenderer.Render(mail.BodyHtml, tracker.Subscriber);
        await _emailSender.SendAsync(new EmailSendRequest(tracker.Subscriber.Email, subject, body, Source: $"Sequence:{tracker.SequenceId}"));

        // Advances regardless of send success/failure — one bad address shouldn't wedge the rest
        // of the drip, same reasoning as CampaignMessageJob not retrying a rejected send.
        var next = await _db.EmailSequenceMails
            .Where(m => m.SequenceId == tracker.SequenceId && m.Order > mail.Order)
            .OrderBy(m => m.Order)
            .FirstOrDefaultAsync();

        tracker.LastMailId = mail.Id;
        if (next is null)
        {
            tracker.Status = EmailSequenceSubscriberStatus.Completed;
            tracker.NextMailId = null;
            tracker.NextExecutionAt = null;
        }
        else
        {
            tracker.NextMailId = next.Id;
            tracker.NextExecutionAt = DateTime.UtcNow.Add(EmailSequenceService.Delay(next));
        }

        tracker.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
