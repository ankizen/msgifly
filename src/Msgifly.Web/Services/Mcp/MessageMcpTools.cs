using System.ComponentModel;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Msgifly.Web.Data;
using Msgifly.Web.Hubs;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.ApiKeys;
using Msgifly.Web.Services.WhatsApp;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Services.Mcp;

/// <summary>Modeled on Controllers/Api/V1/MessagesController.cs's type=template branch — that
/// controller is phone-addressed and already built for a non-cookie machine caller, unlike the
/// Admin dashboard's contact-id-addressed, CSRF-protected SendTemplate actions.</summary>
[McpServerToolType]
public class MessageMcpTools
{
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MessageMcpTools(
        ApplicationDbContext db,
        IWhatsAppService whatsAppService,
        IHubContext<ChatHub> hubContext,
        ICurrentWorkspaceAccessor workspaceAccessor,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _hubContext = hubContext;
        _workspaceAccessor = workspaceAccessor;
        _httpContextAccessor = httpContextAccessor;
    }

    [McpServerTool(Name = "send_template_message")]
    [Description("Sends an approved WhatsApp template message to one phone number right now. The template must already be status 'Approved' — check with list_templates first. The send shows up live in the Msgifly Chat inbox like any other message.")]
    public async Task<object> SendTemplateMessageAsync(
        [Description("Recipient's WhatsApp phone number, with country code, digits only (e.g. 971501234567)")] string phone,
        [Description("Exact template name, from list_templates")] string templateName,
        [Description("Value for the template's TEXT header {{1}}, or a media URL for image/video/document headers. Omit if the template has no header.")] string? headerParam = null,
        [Description("Values for the body's {{1}}, {{2}}, ... placeholders in order")] List<string>? bodyParams = null)
    {
        _httpContextAccessor.RequireScope(ApiScopes.MessagesSend);

        var template = await _db.WhatsappTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TemplateName == templateName && t.Status == TemplateStatus.Approved);
        if (template is null)
        {
            throw new McpException($"No approved template named '{templateName}' in this workspace. Check list_templates.");
        }

        var isTextHeader = string.Equals(template.HeaderFormat, "TEXT", StringComparison.OrdinalIgnoreCase);
        var request = new TemplateSendRequest
        {
            TemplateName = template.TemplateName,
            Language = template.Language,
            HeaderFormat = template.HeaderFormat,
            HeaderText = isTextHeader ? headerParam : null,
            HeaderMediaUrl = isTextHeader ? null : headerParam,
            BodyParams = (bodyParams ?? []).Take(template.BodyParamsCount).ToList(),
        };

        var sendResult = await _whatsAppService.SendTemplateMessageAsync(phone, request);
        if (!sendResult.Success)
        {
            return new { success = false, error = sendResult.ErrorMessage };
        }

        var rendered = TemplateMessageRenderer.ForChatMessage(template, request);

        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.ReceiverId == phone);
        var contactCreated = false;
        if (chat is null)
        {
            chat = new Models.Entities.Chat { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value, ReceiverId = phone, Name = phone };
            _db.Chats.Add(chat);
            contactCreated = true;
        }

        chat.LastMessage = rendered.DisplayText;
        chat.LastMessageTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var message = new ChatMessage
        {
            ChatId = chat.Id,
            SenderId = chat.WaNoId ?? "mcp",
            Message = rendered.DisplayText,
            MessageType = rendered.MediaMessageType ?? "text",
            Url = rendered.MediaUrl,
            WhatsappMessageId = sendResult.Data,
            Status = MessageDeliveryStatus.Sent,
            SentAt = DateTime.UtcNow,
            TemplateName = template.TemplateName,
            TimeSent = DateTime.UtcNow,
            IsRead = true,
        };
        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync();

        var dto = new ChatMessageDto(message.Id, message.SenderId, message.Message, message.MessageType, message.TimeSent, true, message.Status.ToString(), message.Url);
        await _hubContext.Clients.All.SendAsync("ReceiveMessage", chat.Id, dto);

        return new
        {
            success = true,
            messageId = message.Id,
            whatsappMessageId = sendResult.Data,
            conversationId = chat.Id,
            contactCreated,
        };
    }
}
