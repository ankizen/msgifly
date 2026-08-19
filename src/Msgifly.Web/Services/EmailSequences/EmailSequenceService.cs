using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.EmailSequences;

/// <summary>
/// Enrollment/cancellation for EmailSequence — the strictly linear drip system, kept deliberately
/// simpler than EmailAutomationEngine (no branching, no conditions, just a cursor advanced by
/// EmailSequenceDispatchJob's minutely sweep). Matches FluentCRM's own Sequences product shape.
/// </summary>
public class EmailSequenceService
{
    private readonly ApplicationDbContext _db;

    public EmailSequenceService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>No-ops if already enrolled, or if the sequence has no mails yet.</summary>
    public async Task SubscribeAsync(int sequenceId, int subscriberId)
    {
        var alreadyEnrolled = await _db.EmailSequenceSubscribers
            .AnyAsync(s => s.SequenceId == sequenceId && s.SubscriberId == subscriberId);
        if (alreadyEnrolled)
        {
            return;
        }

        var firstMail = await _db.EmailSequenceMails
            .Where(m => m.SequenceId == sequenceId)
            .OrderBy(m => m.Order)
            .FirstOrDefaultAsync();
        if (firstMail is null)
        {
            return;
        }

        _db.EmailSequenceSubscribers.Add(new EmailSequenceSubscriber
        {
            SequenceId = sequenceId,
            SubscriberId = subscriberId,
            Status = EmailSequenceSubscriberStatus.Active,
            NextMailId = firstMail.Id,
            NextExecutionAt = DateTime.UtcNow.Add(Delay(firstMail)),
        });
        await _db.SaveChangesAsync();
    }

    public async Task UnsubscribeAsync(int sequenceId, int subscriberId)
    {
        var tracker = await _db.EmailSequenceSubscribers.FirstOrDefaultAsync(
            s => s.SequenceId == sequenceId && s.SubscriberId == subscriberId && s.Status == EmailSequenceSubscriberStatus.Active);
        if (tracker is null)
        {
            return;
        }

        tracker.Status = EmailSequenceSubscriberStatus.Cancelled;
        tracker.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    /// <summary>Called when a subscriber globally unsubscribes — pulls them out of every active
    /// sequence, not just one.</summary>
    public async Task UnsubscribeAllAsync(int subscriberId)
    {
        var trackers = await _db.EmailSequenceSubscribers
            .Where(s => s.SubscriberId == subscriberId && s.Status == EmailSequenceSubscriberStatus.Active)
            .ToListAsync();
        if (trackers.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var tracker in trackers)
        {
            tracker.Status = EmailSequenceSubscriberStatus.Cancelled;
            tracker.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();
    }

    public static TimeSpan Delay(EmailSequenceMail mail) => mail.DelayUnit switch
    {
        "days" => TimeSpan.FromDays(mail.DelayAmount),
        "hours" => TimeSpan.FromHours(mail.DelayAmount),
        _ => TimeSpan.FromMinutes(mail.DelayAmount),
    };
}
