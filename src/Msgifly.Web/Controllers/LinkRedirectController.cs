using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Controllers;

/// <summary>
/// Real per-recipient click tracking for WhatsApp template URL buttons — WhatsApp itself gives no
/// signal at all when a URL button is tapped (unlike Quick Reply buttons, which generate a genuine
/// inbound webhook message). Template URL buttons are rewritten at submission time to point here
/// instead of the real destination (see WhatsAppService.BuildTemplateComponentsAsync), with a
/// per-send token as the dynamic {{1}} suffix, so a tap becomes an observable HTTP request before
/// this redirects on to where the button actually promised to go.
///
/// Lives on whichever domain a workspace configured (e.g. link.salonsteps.com) — Coolify/Traefik
/// routes every domain in the app's domain list to this same container, so this controller never
/// needs to know or care which domain a request arrived on; the token alone resolves everything.
/// </summary>
[AllowAnonymous]
[Route("r")]
public class LinkRedirectController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public LinkRedirectController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    [HttpGet("__verify")]
    public IActionResult Verify() => Ok("ok");

    [HttpGet("{token}")]
    public async Task<IActionResult> GoToDestination(string token)
    {
        var click = await _db.TemplateButtonClicks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Token == token);
        if (click is null || !Uri.TryCreate(click.DestinationUrl, UriKind.Absolute, out var destination))
        {
            return NotFound();
        }

        // Must be set before touching any workspace-scoped table below — no cookie session here,
        // same reason WhatsAppWebhookController resolves it manually before processing a payload.
        _workspaceAccessor.WorkspaceId = click.WorkspaceId;

        var timestamp = DateTime.UtcNow;
        click.ClickCount++;
        click.FirstClickedAt ??= timestamp;
        click.LastClickedAt = timestamp;

        if (!string.IsNullOrEmpty(click.WhatsappMessageId))
        {
            // Mirrors WhatsAppWebhookController.ProcessReplyAttributionAsync exactly, so the
            // Templates Report page and the automation "TemplateClicked" condition — both already
            // keyed off these same columns — pick up real URL clicks with no changes of their own.
            var chatMessage = await _db.ChatMessages.FirstOrDefaultAsync(m => m.WhatsappMessageId == click.WhatsappMessageId);
            if (chatMessage is not null)
            {
                chatMessage.Clicked = true;
                chatMessage.ClickedButtonText = click.ButtonText;
                chatMessage.UpdatedAt = timestamp;
            }

            var campaignDetail = await _db.CampaignDetails.FirstOrDefaultAsync(d => d.WhatsappMessageId == click.WhatsappMessageId);
            if (campaignDetail is not null)
            {
                campaignDetail.Clicked = true;
                campaignDetail.ClickedButtonText = click.ButtonText;
                campaignDetail.RepliedAt ??= timestamp;
                campaignDetail.UpdatedAt = timestamp;
            }
        }

        await _db.SaveChangesAsync();
        return Redirect(destination.ToString());
    }
}
