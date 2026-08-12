using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Authorization;
using Msgifly.Web.Data;
using Msgifly.Web.Hubs;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.ApiKeys;
using Msgifly.Web.Services.WhatsApp;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Controllers.Api.V1;

/// <summary>
/// POST /api/v1/messages — the headline public endpoint: send a WhatsApp message from an
/// external script/automation by phone number (not an internal chat id, which a machine caller
/// wouldn't have). Finds-or-creates the Chat the same way the inbound webhook does, so a message
/// sent here shows up in the dashboard's live inbox immediately.
/// </summary>
[ApiController]
[Route("api/v1/messages")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public class MessagesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public MessagesController(ApplicationDbContext db, IWhatsAppService whatsAppService, IHubContext<ChatHub> hubContext, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _hubContext = hubContext;
        _workspaceAccessor = workspaceAccessor;
    }

    public record SendMessageRequest(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("media_url")] string? MediaUrl,
        [property: JsonPropertyName("caption")] string? Caption,
        [property: JsonPropertyName("filename")] string? Filename,
        [property: JsonPropertyName("template")] SendTemplatePayload? Template,
        [property: JsonPropertyName("name")] string? Name);

    public record SendTemplatePayload(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("params")] List<string>? Params);

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendMessageRequest request)
    {
        if (!User.HasApiScope(ApiScopes.MessagesSend))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.To))
        {
            return BadRequest(new { error = "bad_request", message = "'to' is required." });
        }

        var type = string.IsNullOrWhiteSpace(request.Type) ? "text" : request.Type!.ToLowerInvariant();

        WhatsAppResult<string> sendResult;
        string storedMessage;
        string storedType = type;
        string? storedUrl = null;

        switch (type)
        {
            case "text":
                if (string.IsNullOrWhiteSpace(request.Text))
                {
                    return BadRequest(new { error = "bad_request", message = "'text' is required for type=text." });
                }

                sendResult = await _whatsAppService.SendPlainTextMessageAsync(request.To, request.Text);
                storedMessage = request.Text;
                break;

            case "template":
                if (request.Template is null || string.IsNullOrWhiteSpace(request.Template.Name))
                {
                    return BadRequest(new { error = "bad_request", message = "'template.name' is required for type=template." });
                }

                sendResult = await _whatsAppService.SendTemplateMessageAsync(request.To, new TemplateSendRequest
                {
                    TemplateName = request.Template.Name,
                    Language = request.Template.Language ?? "en_US",
                    BodyParams = request.Template.Params ?? [],
                });
                storedMessage = $"[Template: {request.Template.Name}]";
                break;

            case "image" or "video" or "document" or "audio":
                if (string.IsNullOrWhiteSpace(request.MediaUrl))
                {
                    return BadRequest(new { error = "bad_request", message = "'media_url' is required for media types." });
                }

                sendResult = await _whatsAppService.SendMediaMessageAsync(request.To, new MediaMessageRequest
                {
                    MediaType = type,
                    Link = request.MediaUrl,
                    Caption = request.Caption,
                    Filename = request.Filename,
                });
                storedMessage = request.Caption ?? $"[{type}]";
                storedUrl = request.MediaUrl;
                break;

            default:
                return BadRequest(new { error = "bad_request", message = $"Unsupported type '{type}'." });
        }

        if (!sendResult.Success)
        {
            return UnprocessableEntity(new { error = "send_failed", message = sendResult.ErrorMessage });
        }

        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.ReceiverId == request.To);
        var contactCreated = false;
        if (chat is null)
        {
            chat = new Chat { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value, ReceiverId = request.To, Name = request.Name ?? request.To };
            _db.Chats.Add(chat);
            contactCreated = true;
        }

        chat.LastMessage = storedMessage;
        chat.LastMessageTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var message = new ChatMessage
        {
            ChatId = chat.Id,
            SenderId = chat.WaNoId ?? "api",
            Message = storedMessage,
            MessageType = storedType,
            Url = storedUrl,
            WhatsappMessageId = sendResult.Data,
            Status = MessageDeliveryStatus.Sent,
            TimeSent = DateTime.UtcNow,
            IsRead = true,
        };
        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync();

        var dto = new ChatMessageDto(message.Id, message.SenderId, message.Message, message.MessageType, message.TimeSent, true, message.Status.ToString(), message.Url);
        await _hubContext.Clients.All.SendAsync("ReceiveMessage", chat.Id, dto);

        return StatusCode(StatusCodes.Status201Created, new
        {
            data = new
            {
                message_id = message.Id,
                whatsapp_message_id = sendResult.Data,
                conversation_id = chat.Id,
                contact_created = contactCreated,
            },
        });
    }
}
