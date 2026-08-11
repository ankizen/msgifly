using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Msgifly.Web.Services.Settings;

namespace Msgifly.Web.Controllers;

/// <summary>
/// Meta calls this directly (no user session), so it's unauthenticated and lives outside the
/// Admin area. GET is the webhook verification handshake; POST is where inbound message/status
/// events will eventually be processed (see WHATSMARK_MASTER_REFERENCE.md §5.3) — that message
/// storage/bot-trigger pipeline is a later phase, so for now POST just acknowledges receipt so
/// Meta doesn't disable the subscription for repeated failures.
/// </summary>
[AllowAnonymous]
[Route("whatsapp/webhook")]
public class WhatsAppWebhookController : Controller
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(ISettingsService settingsService, ILogger<WhatsAppWebhookController> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken)
    {
        var settings = await _settingsService.GetAsync<WhatsAppSettings>(nameof(WhatsAppSettings));

        if (mode == "subscribe"
            && !string.IsNullOrEmpty(settings.WebhookVerifyToken)
            && verifyToken == settings.WebhookVerifyToken)
        {
            return Content(challenge ?? string.Empty, "text/plain");
        }

        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost]
    public async Task<IActionResult> Receive()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();
        _logger.LogInformation("WhatsApp webhook payload received ({Length} bytes)", payload.Length);

        // TODO(Phase 5 — chat/inbox): parse `payload`, store inbound messages/status updates,
        // and trigger the bot-matching engine. See master doc §5.3-§5.5 for the full pipeline.

        return Ok();
    }
}
