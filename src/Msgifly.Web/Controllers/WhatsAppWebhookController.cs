using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Hubs;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.Bots;
using Msgifly.Web.Services.Settings;
using Msgifly.Web.Services.WhatsApp;

namespace Msgifly.Web.Controllers;

/// <summary>
/// Meta calls this directly (no user session), so it's unauthenticated and lives outside the
/// Admin area. GET is the webhook verification handshake; POST processes inbound messages and
/// delivery-status updates — see master doc §5.3-§5.5 for the original's equivalent pipeline.
/// This phase wires up contact resolution/auto-lead, Chat/ChatMessage storage, and bot
/// auto-replies; the live chat *UI* (SignalR, the inbox screen itself) is a later phase — bots
/// already work end-to-end even before that UI exists.
/// Known gap: only text/button/interactive message types are read for bot-matching purposes;
/// media messages (image/audio/document/video) are stored with a placeholder and can still
/// trigger first-message/catch-all bots, but their content isn't downloaded in this phase.
/// </summary>
[AllowAnonymous]
[Route("whatsapp/webhook")]
public class WhatsAppWebhookController : Controller
{
    private readonly ISettingsService _settingsService;
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly BotMatchingService _botMatchingService;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        ISettingsService settingsService,
        ApplicationDbContext db,
        IWhatsAppService whatsAppService,
        BotMatchingService botMatchingService,
        IHubContext<ChatHub> hubContext,
        ILogger<WhatsAppWebhookController> logger)
    {
        _settingsService = settingsService;
        _db = db;
        _whatsAppService = whatsAppService;
        _botMatchingService = botMatchingService;
        _hubContext = hubContext;
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

        try
        {
            var value = JsonNode.Parse(payload)?["entry"]?.AsArray().FirstOrDefault()?["changes"]?.AsArray().FirstOrDefault()?["value"];
            if (value is null)
            {
                return Ok();
            }

            var messages = value["messages"]?.AsArray();
            var statuses = value["statuses"]?.AsArray();

            if (messages is { Count: > 0 })
            {
                await ProcessIncomingMessagesAsync(value, messages);
            }
            else if (statuses is { Count: > 0 })
            {
                await ProcessStatusUpdatesAsync(statuses);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process WhatsApp webhook payload ({Length} bytes)", payload.Length);
        }

        return Ok();
    }

    private async Task ProcessIncomingMessagesAsync(JsonNode value, JsonArray messages)
    {
        var businessPhoneNumberId = value["metadata"]?["phone_number_id"]?.GetValue<string>();
        var contactName = value["contacts"]?.AsArray().FirstOrDefault()?["profile"]?["name"]?.GetValue<string>();
        var wmSettings = await _settingsService.GetAsync<WhatsMarkSettings>(nameof(WhatsMarkSettings));

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

            var messageText = ExtractText(message);
            var messageType = message["type"]?.GetValue<string>() ?? "text";

            var chat = await _db.Chats.FirstOrDefaultAsync(c => c.ReceiverId == from);
            var isFirstMessage = chat is null;

            var contact = await ResolveContactAsync(from, contactName, wmSettings);

            if (chat is null)
            {
                chat = new Chat { ReceiverId = from, Name = contactName ?? from };
                _db.Chats.Add(chat);
            }

            chat.Name = contactName ?? chat.Name;
            chat.WaNo = businessPhoneNumberId;
            chat.WaNoId = businessPhoneNumberId;
            chat.LastMessage = messageText;
            chat.LastMessageTime = DateTime.UtcNow;
            chat.UpdatedAt = DateTime.UtcNow;

            // Stop-bot keyword / auto-restart window (mirrors the original's per-chat pause state).
            if (chat.IsBotsStopped && chat.BotStoppedTime is not null
                && DateTime.UtcNow - chat.BotStoppedTime > TimeSpan.FromHours(wmSettings.RestartBotsAfterHours))
            {
                chat.IsBotsStopped = false;
                chat.BotStoppedTime = null;
            }

            var stopKeywords = wmSettings.StopBotKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (stopKeywords.Any(k => string.Equals(k, messageText.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                chat.IsBotsStopped = true;
                chat.BotStoppedTime = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(); // need chat.Id for the message below

            var inboundMessage = new ChatMessage
            {
                ChatId = chat.Id,
                SenderId = from,
                Message = messageText,
                MessageType = messageType,
                WhatsappMessageId = messageId,
                Status = MessageDeliveryStatus.Read,
                TimeSent = DateTime.UtcNow,
                IsRead = false,
            };
            _db.ChatMessages.Add(inboundMessage);
            await _db.SaveChangesAsync();

            await BroadcastMessageAsync(chat, inboundMessage);

            if (!chat.IsBotsStopped && contact is not null)
            {
                await FireMatchingBotsAsync(chat, contact, messageText, isFirstMessage);
            }
        }
    }

    private async Task<Contact?> ResolveContactAsync(string phone, string? name, WhatsMarkSettings wmSettings)
    {
        var digitsOnly = new string(phone.Where(char.IsDigit).ToArray());
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Phone == phone || c.Phone == digitsOnly || c.Phone == "+" + digitsOnly);
        if (contact is not null || !wmSettings.AutoCreateLeadOnInboundMessage)
        {
            return contact;
        }

        var statusId = wmSettings.DefaultLeadStatusId ?? await _db.Statuses.Select(s => (int?)s.Id).FirstOrDefaultAsync();
        var sourceId = wmSettings.DefaultLeadSourceId ?? await _db.Sources.Select(s => (int?)s.Id).FirstOrDefaultAsync();
        if (statusId is null || sourceId is null)
        {
            return null; // no Status/Source configured yet to assign a new lead to
        }

        var nameParts = (name ?? phone).Split(' ', 2);
        contact = new Contact
        {
            FirstName = nameParts[0],
            LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
            Phone = phone,
            Type = ContactType.Lead,
            StatusId = statusId.Value,
            SourceId = sourceId.Value,
            IsEnabled = true,
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();
        return contact;
    }

    private async Task FireMatchingBotsAsync(Chat chat, Contact contact, string messageText, bool isFirstMessage)
    {
        var matches = await _botMatchingService.FindMatchingBotsAsync(contact.Type, messageText, isFirstMessage);
        if (!matches.Any)
        {
            return;
        }

        foreach (var bot in matches.MessageBots)
        {
            var result = await _whatsAppService.SendPlainTextMessageAsync(contact.Phone, ComposeText(bot.HeaderText, bot.ReplyText, bot.FooterText));
            if (result.Success)
            {
                await StoreBotReplyAsync(chat, ComposeText(bot.HeaderText, bot.ReplyText, bot.FooterText));
                bot.SendingCount++;
            }
            else
            {
                _logger.LogWarning("Message bot {BotId} failed to send: {Error}", bot.Id, result.ErrorMessage);
            }
        }

        foreach (var bot in matches.TemplateBots)
        {
            var template = await _db.WhatsappTemplates.FirstOrDefaultAsync(t => t.MetaTemplateId == bot.TemplateId);
            if (template is null)
            {
                continue;
            }

            var headerParams = Services.Campaigns.CampaignParamResolver.ResolveAll(bot.HeaderParamsJson, contact);
            var bodyParams = Services.Campaigns.CampaignParamResolver.ResolveAll(bot.BodyParamsJson, contact);

            var result = await _whatsAppService.SendTemplateMessageAsync(contact.Phone, new TemplateSendRequest
            {
                TemplateName = template.TemplateName,
                Language = template.Language,
                HeaderFormat = template.HeaderFormat,
                HeaderText = headerParams.Count > 0 ? headerParams[0] : null,
                HeaderMediaUrl = bot.FileName,
                BodyParams = bodyParams,
            });

            if (result.Success)
            {
                await StoreBotReplyAsync(chat, $"[Template: {template.TemplateName}] {string.Join(" / ", bodyParams)}");
                bot.SendingCount++;
            }
            else
            {
                _logger.LogWarning("Template bot {BotId} failed to send: {Error}", bot.Id, result.ErrorMessage);
            }
        }

        await _db.SaveChangesAsync();
    }

    private async Task StoreBotReplyAsync(Chat chat, string text)
    {
        var reply = new ChatMessage
        {
            ChatId = chat.Id,
            SenderId = chat.WaNoId ?? "bot",
            Message = text,
            MessageType = "text",
            Status = MessageDeliveryStatus.Sent,
            TimeSent = DateTime.UtcNow,
            IsRead = true,
        };
        _db.ChatMessages.Add(reply);

        chat.LastMessage = text;
        chat.LastMessageTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await BroadcastMessageAsync(chat, reply);
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
            message.Status.ToString());

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

            var errorDetail = status!["errors"]?.AsArray().FirstOrDefault()?["title"]?.GetValue<string>();

            var chatMessage = await _db.ChatMessages.FirstOrDefaultAsync(m => m.WhatsappMessageId == wamid);
            if (chatMessage is not null)
            {
                chatMessage.Status = deliveryStatus.Value;
                chatMessage.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("MessageStatusUpdated", chatMessage.ChatId, chatMessage.Id, chatMessage.Status.ToString());
            }

            var campaignDetail = await _db.CampaignDetails.FirstOrDefaultAsync(d => d.WhatsappMessageId == wamid);
            if (campaignDetail is not null)
            {
                campaignDetail.DeliveryStatus = deliveryStatus.Value;
                campaignDetail.ResponseMessage = errorDetail ?? campaignDetail.ResponseMessage;
                campaignDetail.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
    }

    private static string ExtractText(JsonNode message)
    {
        var type = message["type"]?.GetValue<string>();
        return type switch
        {
            "text" => message["text"]?["body"]?.GetValue<string>() ?? string.Empty,
            "button" => message["button"]?["text"]?.GetValue<string>() ?? string.Empty,
            "interactive" => message["interactive"]?["button_reply"]?["title"]?.GetValue<string>()
                ?? message["interactive"]?["list_reply"]?["title"]?.GetValue<string>()
                ?? string.Empty,
            _ => $"[{type}]",
        };
    }

    private static string ComposeText(string? header, string body, string? footer) =>
        string.Join("\n\n", new[] { header, body, footer }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
