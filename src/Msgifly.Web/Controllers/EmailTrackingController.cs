using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.EmailSequences;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Controllers;

/// <summary>
/// Public, token-based tracking endpoints for campaign emails — open pixel, click redirect,
/// unsubscribe. Modeled directly on LinkRedirectController: resolve the EmailCampaignRecipient by
/// its TrackingToken with IgnoreQueryFilters() (no cookie session here), bootstrap
/// ICurrentWorkspaceAccessor manually before touching any other workspace-scoped table.
/// </summary>
[AllowAnonymous]
[Route("e")]
public class EmailTrackingController : Controller
{
    // The standard 1x1 transparent GIF used industry-wide for open-tracking pixels.
    private static readonly byte[] TransparentPixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBTAA7");

    private readonly ApplicationDbContext _db;
    private readonly EmailSequenceService _sequenceService;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public EmailTrackingController(ApplicationDbContext db, EmailSequenceService sequenceService, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _sequenceService = sequenceService;
        _workspaceAccessor = workspaceAccessor;
    }

    [HttpGet("o/{token}")]
    public async Task<IActionResult> Open(string token)
    {
        var recipient = await _db.EmailCampaignRecipients.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TrackingToken == token);
        if (recipient is not null)
        {
            _workspaceAccessor.WorkspaceId = await ResolveWorkspaceIdAsync(recipient.CampaignId);
            if (!recipient.IsOpened)
            {
                recipient.IsOpened = true;
                recipient.OpenedAt = DateTime.UtcNow;
                recipient.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }

        return File(TransparentPixel, "image/gif");
    }

    [HttpGet("c/{token}")]
    public async Task<IActionResult> Click(string token, string to)
    {
        if (!Uri.TryCreate(to, UriKind.Absolute, out var destination) || (destination.Scheme != "http" && destination.Scheme != "https"))
        {
            return NotFound();
        }

        var recipient = await _db.EmailCampaignRecipients.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TrackingToken == token);
        if (recipient is not null)
        {
            _workspaceAccessor.WorkspaceId = await ResolveWorkspaceIdAsync(recipient.CampaignId);
            recipient.IsClicked = true;
            recipient.ClickCount++;
            recipient.ClickedAt ??= DateTime.UtcNow;
            recipient.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Redirect(destination.ToString());
    }

    [HttpGet("u/{token}")]
    public async Task<IActionResult> Unsubscribe(string token)
    {
        var recipient = await _db.EmailCampaignRecipients.IgnoreQueryFilters()
            .Include(r => r.Subscriber)
            .FirstOrDefaultAsync(r => r.TrackingToken == token);
        if (recipient is null || recipient.Subscriber is null)
        {
            return NotFound();
        }

        _workspaceAccessor.WorkspaceId = await ResolveWorkspaceIdAsync(recipient.CampaignId);

        recipient.IsUnsubscribed = true;
        recipient.UnsubscribedAt = DateTime.UtcNow;
        recipient.UpdatedAt = DateTime.UtcNow;

        // Unsubscribe is global, not per-campaign — matches FluentCRM's own semantics.
        recipient.Subscriber.EmailStatus = EmailSubscriberStatus.Unsubscribed;
        recipient.Subscriber.UpdatedAt = DateTime.UtcNow;

        await _sequenceService.UnsubscribeAllAsync(recipient.Subscriber.Id);
        await _db.SaveChangesAsync();

        return View("Unsubscribed", recipient.Subscriber.Email);
    }

    private async Task<int> ResolveWorkspaceIdAsync(int campaignId) =>
        await _db.EmailCampaigns.IgnoreQueryFilters().Where(c => c.Id == campaignId).Select(c => c.WorkspaceId).FirstOrDefaultAsync();
}
