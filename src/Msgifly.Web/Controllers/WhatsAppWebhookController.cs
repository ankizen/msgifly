using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Hubs;
using Msgifly.Web.Jobs;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services;
using Msgifly.Web.Services.Automations;
using Msgifly.Web.Services.Chat;
using Msgifly.Web.Services.Settings;
using Msgifly.Web.Services.WhatsApp;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Controllers;

/// <summary>
/// Meta calls this directly (no user session), so it's unauthenticated and lives outside the
/// Admin area. GET is the webhook verification handshake; POST processes inbound messages and
/// delivery-status updates. Wires up contact resolution/auto-lead, Chat/ChatMessage storage, and
/// automation triggers (see AutomationEngine) — the standalone Message Bot / Template Bot system
/// was removed since a one-step Automation already covers everything they could do, and more.
///
/// Multi-tenant note: Meta calls ONE webhook URL for the whole App, shared by every Workspace's
/// WABA — so before touching any workspace-scoped table, Receive() resolves which Workspace the
/// payload belongs to (via entry[].id, the WABA id) and sets ICurrentWorkspaceAccessor itself.
/// Everything downstream (Contacts, Chat, bots, automations) then sees the right tenant's data
/// through the normal EF Core query filters, same as any cookie-scoped Admin request.
///
/// This same URL also receives Facebook Page events (top-level "object": "page" instead of
/// "whatsapp_business_account") — currently just realtime Lead Ads (leadgen) submissions, resolved
/// to a Workspace via FacebookPageId instead of BusinessAccountId. See ProcessPageEntriesAsync.
/// </summary>
[AllowAnonymous]
[Route("whatsapp/webhook")]
public class WhatsAppWebhookController : Controller
{
    private readonly ISettingsService _settingsService;
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly AutomationEngine _automationEngine;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IWebHostEnvironment _environment;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly LeadAdsSyncJob _leadAdsSyncJob;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        ISettingsService settingsService,
        ApplicationDbContext db,
        IWhatsAppService whatsAppService,
        AutomationEngine automationEngine,
        IHubContext<ChatHub> hubContext,
        IWebHostEnvironment environment,
        ICurrentWorkspaceAccessor workspaceAccessor,
        LeadAdsSyncJob leadAdsSyncJob,
        ILogger<WhatsAppWebhookController> logger)
    {
        _settingsService = settingsService;
        _db = db;
        _whatsAppService = whatsAppService;
        _automationEngine = automationEngine;
        _hubContext = hubContext;
        _environment = environment;
        _workspaceAccessor = workspaceAccessor;
        _leadAdsSyncJob = leadAdsSyncJob;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken)
    {
        // The webhook subscription (URL + verify token) is configured once at the Meta App level
        // and shared by every Workspace's WABA — this check is deliberately global, not per-tenant.
        var settings = await _settingsService.GetAsync<MetaAppSettings>(nameof(MetaAppSettings));

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

        try
        {
            // One App-level callback URL receives every subscribed object type — WhatsApp
            // (whatsapp_business_account) and, now, Facebook Page events (page, for realtime Lead
            // Ads submissions) both land here, distinguished only by this top-level field.
            var root = JsonNode.Parse(payload);
            var objectType = root?["object"]?.GetValue<string>();
            var entries = root?["entry"]?.AsArray() ?? [];

            if (objectType == "page")
            {
                await ProcessPageEntriesAsync(entries);
                return Ok();
            }

            // Meta batches every change since the last delivery into one call — a single POST can
            // legitimately carry more than one entry (WABA) and more than one change per entry
            // (e.g. a message plus a template status update together). Only ever looking at
            // entry[0].changes[0] silently dropped everything else in that case.
            foreach (var entry in entries)
            {
                if (entry is null)
                {
                    continue;
                }

                var businessAccountId = entry["id"]?.GetValue<string>();
                var workspace = string.IsNullOrEmpty(businessAccountId)
                    ? null
                    : await _db.Workspaces.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.BusinessAccountId == businessAccountId);
                if (workspace is null)
                {
                    _logger.LogWarning("WhatsApp webhook payload for unknown WABA {WabaId} — no matching Workspace, ignoring.", businessAccountId);
                    continue;
                }

                _workspaceAccessor.WorkspaceId = workspace.Id;

                foreach (var change in entry["changes"]?.AsArray() ?? [])
                {
                    var field = change?["field"]?.GetValue<string>();
                    var value = change?["value"];
                    if (value is null)
                    {
                        continue;
                    }

                    switch (field)
                    {
                        case "messages":
                            var messages = value["messages"]?.AsArray();
                            var statuses = value["statuses"]?.AsArray();
                            if (messages is { Count: > 0 })
                            {
                                await ProcessIncomingMessagesAsync(value, messages, workspace);
                            }
                            else if (statuses is { Count: > 0 })
                            {
                                await ProcessStatusUpdatesAsync(statuses);
                            }

                            break;

                        case "message_template_status_update":
                            await ProcessTemplateStatusUpdateAsync(value);
                            break;

                        default:
                            _logger.LogDebug("Unhandled WhatsApp webhook field {Field}", field ?? "(none)");
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process WhatsApp webhook payload ({Length} bytes)", payload.Length);
        }

        return Ok();
    }

    /// <summary>
    /// Facebook Page events (currently just leadgen — a lead submitted an Instant Form). Requires
    /// the workspace owner to add the "page" object + "leadgen" field to this same callback URL
    /// under Meta App Dashboard -> Webhooks (a one-time step we can't drive via API, same
    /// limitation as ProcessTemplateStatusUpdateAsync's message_template_status_update field) —
    /// the per-Page opt-in itself (subscribed_apps?subscribed_fields=leadgen) IS done via API, by
    /// MetaLeadAdsService.SubscribePageWebhookAsync right after LeadAdsController connects a Page.
    /// </summary>
    private async Task ProcessPageEntriesAsync(JsonArray entries)
    {
        foreach (var entry in entries)
        {
            if (entry is null)
            {
                continue;
            }

            var pageId = entry["id"]?.GetValue<string>();
            var workspace = string.IsNullOrEmpty(pageId)
                ? null
                : await _db.Workspaces.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.FacebookPageId == pageId);
            if (workspace is null)
            {
                _logger.LogWarning("Facebook Page webhook payload for unknown Page {PageId} — no matching Workspace, ignoring.", pageId);
                continue;
            }

            _workspaceAccessor.WorkspaceId = workspace.Id;

            foreach (var change in entry["changes"]?.AsArray() ?? [])
            {
                var field = change?["field"]?.GetValue<string>();
                var value = change?["value"];
                if (value is null)
                {
                    continue;
                }

                if (field == "leadgen")
                {
                    var leadgenId = value["leadgen_id"]?.GetValue<string>();
                    var formId = value["form_id"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(leadgenId) && !string.IsNullOrEmpty(formId))
                    {
                        await _leadAdsSyncJob.ImportSingleLeadAsync(workspace, formId, leadgenId);
                    }
                }
                else
                {
                    _logger.LogDebug("Unhandled Facebook Page webhook field {Field}", field ?? "(none)");
                }
            }
        }
    }

    private async Task ProcessIncomingMessagesAsync(JsonNode value, JsonArray messages, Workspace workspace)
    {
        var businessPhoneNumberId = value["metadata"]?["phone_number_id"]?.GetValue<string>();
        var contactName = value["contacts"]?.AsArray().FirstOrDefault()?["profile"]?["name"]?.GetValue<string>();

        foreach (var message in messages)
        {
            if (message is null)
            {
                continue;
            }

            var messageId = message["id"]?.GetValue<string>();
            var from = message["from"]?.GetValue<string>();
            if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(from))
            {
                continue;
            }

            if (await _db.ChatMessages.AnyAsync(m => m.WhatsappMessageId == messageId))
            {
                continue; // Meta retries delivery at-least-once — already processed.
            }

            var messageType = message["type"]?.GetValue<string>() ?? "text";
            if (messageType == "reaction")
            {
                // A reaction updates an EXISTING message, not a new inbound one — Meta's own
                // retry-at-least-once is naturally idempotent here (re-applying the same emoji to
                // the same message is a no-op), so this doesn't need the WhatsappMessageId dedup
                // check above, which only guards against duplicate rows.
                await ProcessInboundReactionAsync(message);
                continue;
            }

            var messageText = ExtractText(message);
            var mediaUrl = await DownloadInboundMediaAsync(message, messageType);

            var chat = await _db.Chats.FirstOrDefaultAsync(c => c.ReceiverId == from);
            var isFirstMessage = chat is null;

            var contact = await ResolveContactAsync(from, contactName, workspace);

            if (chat is null)
            {
                chat = new Chat { WorkspaceId = workspace.Id, ReceiverId = from, Name = contactName ?? from };
                _db.Chats.Add(chat);
            }

            chat.Name = contactName ?? chat.Name;
            chat.WaNo = businessPhoneNumberId;
            chat.WaNoId = businessPhoneNumberId;
            chat.LastMessage = messageType is "image" or "video" or "audio" or "document" or "sticker"
                ? ChatPreviewText.ForMedia(messageType, messageText)
                : messageText;
            chat.LastMessageTime = DateTime.UtcNow;
            chat.UpdatedAt = DateTime.UtcNow;

            // Stop-bot keyword / auto-restart window (mirrors the original's per-chat pause state).
            if (chat.IsBotsStopped && chat.BotStoppedTime is not null
                && DateTime.UtcNow - chat.BotStoppedTime > TimeSpan.FromHours(workspace.RestartBotsAfterHours))
            {
                chat.IsBotsStopped = false;
                chat.BotStoppedTime = null;
            }

            var stopKeywords = workspace.StopBotKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (stopKeywords.Any(k => string.Equals(k, messageText.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                chat.IsBotsStopped = true;
                chat.BotStoppedTime = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(); // need chat.Id for the message below

            var contextMessageId = message["context"]?["id"]?.GetValue<string>();

            var inboundMessage = new ChatMessage
            {
                ChatId = chat.Id,
                SenderId = from,
                Message = messageText,
                MessageType = messageType,
                Url = mediaUrl,
                WhatsappMessageId = messageId,
                Status = MessageDeliveryStatus.Read,
                TimeSent = DateTime.UtcNow,
                IsRead = false,
                RefMessageId = contextMessageId,
            };
            _db.ChatMessages.Add(inboundMessage);
            await _db.SaveChangesAsync();

            var flowResponseNote = ExtractFlowResponseNote(message);
            if (flowResponseNote is not null && contact is not null)
            {
                _db.ContactNotes.Add(new ContactNote { ContactId = contact.Id, Description = flowResponseNote });
                await _db.SaveChangesAsync();
            }

            if (!string.IsNullOrEmpty(contextMessageId))
            {
                await ProcessReplyAttributionAsync(contextMessageId, messageType, message);
            }

            await BroadcastMessageAsync(chat, inboundMessage);

            if (!chat.IsBotsStopped && !chat.IsBlocked)
            {
                await FireAutomationsAsync(chat, contact, messageText, isFirstMessage, ExtractInteractiveReplyId(message));
            }
        }
    }

    private static string? ExtractInteractiveReplyId(JsonNode message) =>
        message["interactive"]?["button_reply"]?["id"]?.GetValue<string>()
        ?? message["interactive"]?["list_reply"]?["id"]?.GetValue<string>();

    /// <summary>
    /// Meta stamps context.id on any inbound message that's a reply to a specific prior send —
    /// a tapped template quick-reply button (type "button"), a tapped interactive button/list
    /// (type "interactive"), or a plain quoted text reply alike. Correlating that back to the
    /// outbound ChatMessage/CampaignDetail row is what powers per-template click counts and
    /// campaign "who engaged" re-targeting — both need the same context.id lookup, so it's done
    /// once here rather than duplicated per feature.
    /// </summary>
    private async Task ProcessReplyAttributionAsync(string contextMessageId, string messageType, JsonNode message)
    {
        var buttonText = messageType switch
        {
            "button" => message["button"]?["text"]?.GetValue<string>(),
            "interactive" => message["interactive"]?["button_reply"]?["title"]?.GetValue<string>()
                ?? message["interactive"]?["list_reply"]?["title"]?.GetValue<string>(),
            _ => null,
        };
        var isClick = buttonText is not null;
        var timestamp = DateTime.UtcNow;
        var changed = false;

        var repliedToChatMessage = await _db.ChatMessages.FirstOrDefaultAsync(m => m.WhatsappMessageId == contextMessageId);
        if (repliedToChatMessage is not null && isClick)
        {
            repliedToChatMessage.Clicked = true;
            repliedToChatMessage.ClickedButtonText = buttonText;
            repliedToChatMessage.UpdatedAt = timestamp;
            changed = true;
        }

        var repliedToDetail = await _db.CampaignDetails.FirstOrDefaultAsync(d => d.WhatsappMessageId == contextMessageId);
        if (repliedToDetail is not null)
        {
            repliedToDetail.RepliedAt ??= timestamp; // a button tap counts as engagement too, not just free-text replies.
            if (isClick)
            {
                repliedToDetail.Clicked = true;
                repliedToDetail.ClickedButtonText = buttonText;
            }

            repliedToDetail.UpdatedAt = timestamp;
            changed = true;
        }

        if (changed)
        {
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// A customer reacting to (or removing a reaction from) one of our outbound messages, or one
    /// of their own. Meta's payload shape: {"type":"reaction","reaction":{"message_id":"wamid...",
    /// "emoji":"😀"}} — emoji is absent/empty when a reaction is removed. Combined per-message
    /// (not per-person) to match ReactionEmoji's own model — see ChatController.ReactToMessage for
    /// the admin-side equivalent, which broadcasts the same SignalR event.
    /// </summary>
    private async Task ProcessInboundReactionAsync(JsonNode message)
    {
        var reactedMessageId = message["reaction"]?["message_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(reactedMessageId))
        {
            return;
        }

        var target = await _db.ChatMessages.FirstOrDefaultAsync(m => m.WhatsappMessageId == reactedMessageId);
        if (target is null)
        {
            return;
        }

        var emoji = message["reaction"]?["emoji"]?.GetValue<string>();
        target.ReactionEmoji = string.IsNullOrEmpty(emoji) ? null : emoji;
        target.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("MessageReactionUpdated", target.ChatId, target.Id, target.ReactionEmoji);
    }

    private async Task FireAutomationsAsync(Chat chat, Contact? contact, string messageText, bool isFirstMessage, string? interactiveReplyId)
    {
        var context = new AutomationContext { MessageText = messageText, ChatId = chat.Id, InteractiveReplyId = interactiveReplyId };

        await _automationEngine.RunForTriggerAsync(AutomationTriggerType.InboundMessage, contact?.Id, context);
        if (isFirstMessage)
        {
            await _automationEngine.RunForTriggerAsync(AutomationTriggerType.FirstInboundMessage, contact?.Id, context);
        }

        await _automationEngine.RunForTriggerAsync(AutomationTriggerType.KeywordMatch, contact?.Id, context);

        if (!string.IsNullOrEmpty(interactiveReplyId))
        {
            await _automationEngine.RunForTriggerAsync(AutomationTriggerType.InteractiveReply, contact?.Id, context);
        }
    }

    private static readonly HashSet<string> MediaMessageTypes = ["image", "video", "audio", "document", "sticker"];

    /// <summary>
    /// Inbound media only ever comes as a media_id, valid for a couple of weeks on Meta's CDN —
    /// so it has to be resolved and downloaded right away or it's effectively lost. Saved under
    /// wwwroot so the chat UI can just <img>/<video>/<a> it like any other static asset.
    /// </summary>
    private async Task<string?> DownloadInboundMediaAsync(JsonNode message, string messageType)
    {
        if (!MediaMessageTypes.Contains(messageType))
        {
            return null;
        }

        var mediaId = message[messageType]?["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(mediaId))
        {
            return null;
        }

        var infoResult = await _whatsAppService.GetMediaInfoAsync(mediaId);
        if (!infoResult.Success)
        {
            _logger.LogWarning("Could not resolve inbound media {MediaId}: {Error}", mediaId, infoResult.ErrorMessage);
            return null;
        }

        var downloadResult = await _whatsAppService.DownloadMediaBytesAsync(infoResult.Data!.Url);
        if (!downloadResult.Success)
        {
            _logger.LogWarning("Could not download inbound media {MediaId}: {Error}", mediaId, downloadResult.ErrorMessage);
            return null;
        }

        var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "chat");
        Directory.CreateDirectory(uploadsDir);
        var extension = MimeTypeToExtension(infoResult.Data.MimeType);
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        await System.IO.File.WriteAllBytesAsync(Path.Combine(uploadsDir, storedFileName), downloadResult.Data!);

        return $"/uploads/chat/{storedFileName}";
    }

    private static string MimeTypeToExtension(string mimeType) => mimeType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "video/mp4" => ".mp4",
        "video/3gpp" => ".3gp",
        "audio/aac" => ".aac",
        "audio/mp4" => ".m4a",
        "audio/mpeg" => ".mp3",
        "audio/amr" => ".amr",
        "audio/ogg" => ".ogg",
        "application/pdf" => ".pdf",
        "application/vnd.ms-excel" => ".xls",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "application/msword" => ".doc",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "text/plain" => ".txt",
        _ => string.Empty,
    };

    private async Task<Contact?> ResolveContactAsync(string phone, string? name, Workspace workspace)
    {
        var normalized = PhoneNumberNormalizer.Normalize(phone);
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Phone == phone || c.Phone == normalized || c.Phone == "+" + normalized);
        if (contact is not null || !workspace.AutoCreateLeadOnInboundMessage)
        {
            return contact;
        }

        var statusId = workspace.DefaultLeadStatusId ?? await _db.Statuses.Select(s => (int?)s.Id).FirstOrDefaultAsync();
        var sourceId = workspace.DefaultLeadSourceId ?? await _db.Sources.Select(s => (int?)s.Id).FirstOrDefaultAsync();
        if (statusId is null || sourceId is null)
        {
            return null; // no Status/Source configured yet to assign a new lead to
        }

        var nameParts = (name ?? phone).Split(' ', 2);
        contact = new Contact
        {
            WorkspaceId = workspace.Id,
            FirstName = nameParts[0],
            LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
            Phone = normalized,
            Type = ContactType.Lead,
            StatusId = statusId.Value,
            SourceId = sourceId.Value,
            IsEnabled = true,
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();
        await _automationEngine.RunForTriggerAsync(AutomationTriggerType.NewContactCreated, contact.Id, new AutomationContext());
        return contact;
    }

    private async Task BroadcastMessageAsync(Chat chat, ChatMessage message)
    {
        var dto = new ChatMessageDto(
            message.Id,
            message.SenderId,
            message.Message,
            message.MessageType,
            message.TimeSent,
            IsOutbound: message.StaffId is not null || message.SenderId != chat.ReceiverId,
            message.Status.ToString(),
            message.Url);

        await _hubContext.Clients.All.SendAsync("ReceiveMessage", chat.Id, dto);
    }

    private async Task ProcessStatusUpdatesAsync(JsonArray statuses)
    {
        foreach (var status in statuses)
        {
            var wamid = status?["id"]?.GetValue<string>();
            var statusText = status?["status"]?.GetValue<string>()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(wamid) || statusText is null)
            {
                continue;
            }

            var deliveryStatus = statusText switch
            {
                "sent" => MessageDeliveryStatus.Sent,
                "delivered" => MessageDeliveryStatus.Delivered,
                "read" => MessageDeliveryStatus.Read,
                "failed" => MessageDeliveryStatus.Failed,
                _ => (MessageDeliveryStatus?)null,
            };

            if (deliveryStatus is null)
            {
                continue;
            }

            var timestamp = DateTime.UtcNow;
            var errorNode = status!["errors"]?.AsArray().FirstOrDefault();
            var errorCode = errorNode?["code"]?.GetValue<int?>();
            var errorTitle = errorNode?["title"]?.GetValue<string>();
            var errorDetail = errorCode is not null && errorTitle is not null ? $"{errorCode}: {errorTitle}" : errorTitle;

            var chatMessage = await _db.ChatMessages.FirstOrDefaultAsync(m => m.WhatsappMessageId == wamid);
            if (chatMessage is not null)
            {
                chatMessage.Status = deliveryStatus.Value;
                switch (deliveryStatus.Value)
                {
                    case MessageDeliveryStatus.Sent: chatMessage.SentAt ??= timestamp; break;
                    case MessageDeliveryStatus.Delivered: chatMessage.DeliveredAt ??= timestamp; break;
                    case MessageDeliveryStatus.Read: chatMessage.ReadAt ??= timestamp; break;
                    case MessageDeliveryStatus.Failed: chatMessage.FailedAt ??= timestamp; break;
                }

                if (errorDetail is not null)
                {
                    chatMessage.StatusDetail = errorDetail;
                }

                chatMessage.UpdatedAt = timestamp;
                await _db.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("MessageStatusUpdated", chatMessage.ChatId, chatMessage.Id, chatMessage.Status.ToString());
            }

            var campaignDetail = await _db.CampaignDetails.FirstOrDefaultAsync(d => d.WhatsappMessageId == wamid);
            if (campaignDetail is not null)
            {
                campaignDetail.DeliveryStatus = deliveryStatus.Value;
                switch (deliveryStatus.Value)
                {
                    case MessageDeliveryStatus.Sent: campaignDetail.SentAt ??= timestamp; break;
                    case MessageDeliveryStatus.Delivered: campaignDetail.DeliveredAt ??= timestamp; break;
                    case MessageDeliveryStatus.Read: campaignDetail.ReadAt ??= timestamp; break;
                    case MessageDeliveryStatus.Failed: campaignDetail.FailedAt ??= timestamp; break;
                }

                campaignDetail.ResponseMessage = errorDetail ?? campaignDetail.ResponseMessage;
                campaignDetail.UpdatedAt = timestamp;
                await _db.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// Meta only sends this field once the workspace owner enables it themselves in the Meta App
    /// Dashboard (WhatsApp → Configuration → Webhook fields) — subscribed_apps has no per-field
    /// toggle we can drive from our side. Once enabled, a template getting approved/rejected/paused
    /// mid-campaign is reflected here immediately instead of waiting on the next manual "Sync".
    /// </summary>
    private async Task ProcessTemplateStatusUpdateAsync(JsonNode value)
    {
        var metaTemplateId = value["message_template_id"] switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v when v.TryGetValue<long>(out var l) => l.ToString(),
            _ => null,
        };
        var templateName = value["message_template_name"]?.GetValue<string>();
        var eventText = value["event"]?.GetValue<string>()?.ToUpperInvariant();
        var reason = value["reason"]?.GetValue<string>();

        if (eventText is null || (metaTemplateId is null && templateName is null))
        {
            return;
        }

        var template = metaTemplateId is not null
            ? await _db.WhatsappTemplates.FirstOrDefaultAsync(t => t.MetaTemplateId == metaTemplateId)
            : await _db.WhatsappTemplates.FirstOrDefaultAsync(t => t.TemplateName == templateName);
        if (template is null)
        {
            _logger.LogWarning("Template status update for unknown template {TemplateId}/{TemplateName}", metaTemplateId, templateName);
            return;
        }

        template.Status = eventText switch
        {
            "APPROVED" => TemplateStatus.Approved,
            "REJECTED" => TemplateStatus.Rejected,
            "PAUSED" => TemplateStatus.Paused,
            "DISABLED" => TemplateStatus.Rejected,
            _ => template.Status,
        };
        template.RejectionReason = eventText == "APPROVED" ? null : reason ?? template.RejectionReason;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private static string ExtractText(JsonNode message)
    {
        var type = message["type"]?.GetValue<string>();
        return type switch
        {
            "text" => message["text"]?["body"]?.GetValue<string>() ?? string.Empty,
            "button" => message["button"]?["text"]?.GetValue<string>() ?? string.Empty,
            "interactive" => message["interactive"]?["nfm_reply"] is not null
                ? "Flow response received"
                : message["interactive"]?["button_reply"]?["title"]?.GetValue<string>()
                    ?? message["interactive"]?["list_reply"]?["title"]?.GetValue<string>()
                    ?? string.Empty,
            "image" or "video" or "document" => message[type]?["caption"]?.GetValue<string>() ?? string.Empty,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// A static WhatsApp Flow submission arrives through this same webhook as a normal inbound
    /// message, type "interactive"/"nfm_reply" — nfm_reply.response_json is a JSON-ENCODED STRING
    /// (not a nested object) holding the screen's field answers. Stored as a Contact note rather
    /// than mapped onto dedicated fields, matching how Lead Ads' BuildAnswersNote already handles
    /// unstructured form Q&amp;A with no fixed schema.
    /// </summary>
    private static string? ExtractFlowResponseNote(JsonNode message)
    {
        var nfmReply = message["interactive"]?["nfm_reply"];
        var responseJson = nfmReply?["response_json"]?.GetValue<string>();
        if (string.IsNullOrEmpty(responseJson))
        {
            return null;
        }

        var flowName = nfmReply?["name"]?.GetValue<string>();
        var lines = new List<string> { $"Flow response{(string.IsNullOrEmpty(flowName) ? "" : $" (\"{flowName}\")")}:" };

        try
        {
            var answers = JsonNode.Parse(responseJson)?.AsObject();
            if (answers is not null)
            {
                foreach (var (key, value) in answers)
                {
                    if (value is null)
                    {
                        continue;
                    }

                    var displayValue = value.GetValueKind() == System.Text.Json.JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
                    if (!string.IsNullOrWhiteSpace(displayValue))
                    {
                        lines.Add($"{key}: {displayValue}");
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            lines.Add(responseJson);
        }

        return lines.Count > 1 ? string.Join('\n', lines) : null;
    }
}
