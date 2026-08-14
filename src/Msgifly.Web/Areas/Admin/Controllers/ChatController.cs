using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Hubs;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services;
using Msgifly.Web.Services.Chat;
using Msgifly.Web.Services.WhatsApp;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

/// <summary>
/// The live chat/inbox backend. Unlike the rest of the admin area this is AJAX/JSON-driven
/// (see Views/Chat/Index.cshtml's Alpine component) rather than full-page-per-action, since a
/// conversation UI needs sub-second updates — same reasoning the original used to justify going
/// outside its component framework for this one screen (master doc §7.1 area 1.4).
/// </summary>
[Area("Admin")]
[Authorize]
public class ChatController : Controller
{
    private const int MessagePageSize = 20;
    private const long MaxUploadBytes = 16 * 1024 * 1024; // Meta's own cap for images/audio/docs (video allows up to 16MB too as of the current Cloud API limits)
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IWebHostEnvironment _environment;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public ChatController(ApplicationDbContext db, IWhatsAppService whatsAppService, IHubContext<ChatHub> hubContext, IWebHostEnvironment environment, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _hubContext = hubContext;
        _environment = environment;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "chat.view,chat.read_only")]
    public async Task<IActionResult> Index()
    {
        ViewData["TemplateOptions"] = await _db.WhatsappTemplates.AsNoTracking()
            .Where(t => t.Status == TemplateStatus.Approved && t.MetaTemplateId != null)
            .OrderBy(t => t.TemplateName)
            .Select(t => new TemplateOption(t.MetaTemplateId!, t.TemplateName, t.HeaderFormat, t.HeaderParamsCount, t.BodyParamsCount, t.FooterParamsCount, t.BodyText))
            .ToListAsync();

        ViewData["StatusOptions"] = await _db.Statuses.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name }).ToListAsync();
        ViewData["SourceOptions"] = await _db.Sources.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name }).ToListAsync();

        return View();
    }

    [HttpGet]
    [Authorize(Policy = "chat.view,chat.read_only")]
    public async Task<IActionResult> GetChats(string? search)
    {
        var query = _db.Chats.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => c.Name.Contains(search) || c.ReceiverId.Contains(search));
        }

        var chats = await query.OrderByDescending(c => c.LastMessageTime).Take(200).ToListAsync();
        var receiverIds = chats.Select(c => c.ReceiverId).ToList();

        // GroupBy + take one, not a straight ToDictionaryAsync keyed on Phone: nothing at the DB
        // level enforces Phone uniqueness, so two Contact rows can (and, before
        // PhoneNumberNormalizer, sometimes did) share the same number — a straight
        // ToDictionaryAsync throws ArgumentException on the second matching row instead of just
        // picking one, which took the whole chat list down.
        //
        // Also carries the Contact's own name — once someone's saved as a Contact, whatever the
        // CRM has for them (which the admin can rename freely) should win over Chat.Name, which
        // is just whatever WhatsApp itself last reported as that number's own profile name.
        var contactInfo = await _db.Contacts.AsNoTracking()
            .Where(c => receiverIds.Contains(c.Phone))
            .GroupBy(c => c.Phone)
            .Select(g => new { Phone = g.Key, First = g.First() })
            .ToDictionaryAsync(x => x.Phone, x => new { x.First.Type, Name = (x.First.FirstName + " " + x.First.LastName).Trim() });

        var unreadCounts = await _db.ChatMessages.AsNoTracking()
            .Where(m => chats.Select(c => c.Id).Contains(m.ChatId) && !m.IsRead && m.StaffId == null)
            .GroupBy(m => m.ChatId)
            .Select(g => new { ChatId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ChatId, x => x.Count);

        // Meta's 24-hour customer service window is keyed off the customer's own last message,
        // not ours — SenderId == the chat's own ReceiverId (matching ToDto's IsOutbound formula
        // exactly) is what actually distinguishes "the customer sent this" from an
        // automation/API/bot reply that also happens to carry no StaffId.
        var lastInboundTimes = await _db.ChatMessages.AsNoTracking()
            .Where(m => chats.Select(c => c.Id).Contains(m.ChatId) && m.StaffId == null && m.SenderId == m.Chat.ReceiverId)
            .GroupBy(m => m.ChatId)
            .Select(g => new { ChatId = g.Key, LastInbound = g.Max(m => m.TimeSent) })
            .ToDictionaryAsync(x => x.ChatId, x => x.LastInbound);

        var now = DateTime.UtcNow;
        var result = chats.Select(c =>
        {
            var contact = contactInfo.GetValueOrDefault(c.ReceiverId);
            return new ChatSummaryDto(
                c.Id,
                !string.IsNullOrWhiteSpace(contact?.Name) ? contact.Name : c.Name,
                c.ReceiverId,
                c.LastMessage,
                c.LastMessageTime,
                unreadCounts.GetValueOrDefault(c.Id),
                contact is not null ? contact.Type.ToString() : "Unknown",
                c.IsBotsStopped,
                c.IsBlocked,
                lastInboundTimes.TryGetValue(c.Id, out var lastInbound) && now - lastInbound < TimeSpan.FromHours(24));
        });

        return Json(result);
    }

    [HttpGet]
    [Authorize(Policy = "chat.view,chat.read_only")]
    public async Task<IActionResult> GetMessages(int chatId, int? beforeId)
    {
        var chat = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat is null)
        {
            return NotFound();
        }

        var query = _db.ChatMessages.AsNoTracking().Where(m => m.ChatId == chatId);
        if (beforeId is not null)
        {
            query = query.Where(m => m.Id < beforeId);
        }

        var messages = await query.OrderByDescending(m => m.Id).Take(MessagePageSize).ToListAsync();
        messages.Reverse();

        var result = messages.Select(m => ToDto(m, chat));
        return Json(result);
    }

    [HttpPost]
    [Authorize(Policy = "chat.view")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessage(int chatId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest("Message text is required.");
        }

        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat is null)
        {
            return NotFound();
        }

        if (chat.IsBlocked)
        {
            return BadRequest("This contact is blocked — unblock them first to send a message.");
        }

        if (!await IsWindowOpenAsync(chat))
        {
            return BadRequest("The 24-hour window is closed — this contact hasn't messaged in over a day, so only a template message can reach them now.");
        }

        var result = await _whatsAppService.SendPlainTextMessageAsync(chat.ReceiverId, text);
        if (!result.Success)
        {
            return BadRequest(result.ErrorMessage);
        }

        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;

        var message = new ChatMessage
        {
            ChatId = chat.Id,
            SenderId = chat.WaNoId ?? "agent",
            Message = text,
            MessageType = "text",
            WhatsappMessageId = result.Data,
            StaffId = userId,
            Status = MessageDeliveryStatus.Sent,
            TimeSent = DateTime.UtcNow,
            IsRead = true,
        };
        _db.ChatMessages.Add(message);

        chat.LastMessage = text;
        chat.LastMessageTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dto = ToDto(message, chat);
        await _hubContext.Clients.All.SendAsync("ReceiveMessage", chatId, dto);

        return Json(dto);
    }

    [HttpPost]
    [Authorize(Policy = "chat.view")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMedia(int chatId, IFormFile file, string? caption)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("Choose a file to send.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return BadRequest("File is larger than WhatsApp's 16 MB limit.");
        }

        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat is null)
        {
            return NotFound();
        }

        if (chat.IsBlocked)
        {
            return BadRequest("This contact is blocked — unblock them first to send a message.");
        }

        if (!await IsWindowOpenAsync(chat))
        {
            return BadRequest("The 24-hour window is closed — this contact hasn't messaged in over a day, so only a template message can reach them now.");
        }

        var mediaType = ResolveMediaType(file.ContentType);
        var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "chat");
        Directory.CreateDirectory(uploadsDir);
        var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var absolutePath = Path.Combine(uploadsDir, storedFileName);

        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        var publicUrl = $"{Request.Scheme}://{Request.Host}/uploads/chat/{storedFileName}";

        var sendResult = await _whatsAppService.SendMediaMessageAsync(chat.ReceiverId, new MediaMessageRequest
        {
            MediaType = mediaType,
            Link = publicUrl,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption,
            Filename = mediaType == "document" ? file.FileName : null,
        });

        if (!sendResult.Success)
        {
            System.IO.File.Delete(absolutePath);
            return BadRequest(sendResult.ErrorMessage);
        }

        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;

        var message = new ChatMessage
        {
            ChatId = chat.Id,
            SenderId = chat.WaNoId ?? "agent",
            Message = string.IsNullOrWhiteSpace(caption) ? string.Empty : caption,
            MessageType = mediaType,
            Url = $"/uploads/chat/{storedFileName}",
            WhatsappMessageId = sendResult.Data,
            StaffId = userId,
            Status = MessageDeliveryStatus.Sent,
            TimeSent = DateTime.UtcNow,
            IsRead = true,
        };
        _db.ChatMessages.Add(message);

        chat.LastMessage = ChatPreviewText.ForMedia(mediaType, message.Message);
        chat.LastMessageTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dto = ToDto(message, chat);
        await _hubContext.Clients.All.SendAsync("ReceiveMessage", chatId, dto);

        return Json(dto);
    }

    private static string ResolveMediaType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return "document";
        }

        if (contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase))
        {
            return "sticker";
        }

        var prefix = contentType.Split('/')[0].ToLowerInvariant();
        return prefix switch
        {
            "image" => "image",
            "video" => "video",
            "audio" => "audio",
            _ => "document",
        };
    }

    [HttpPost]
    [Authorize(Policy = "chat.view,chat.read_only")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsRead(int chatId)
    {
        var unread = await _db.ChatMessages
            .Where(m => m.ChatId == chatId && !m.IsRead && m.StaffId == null)
            .ToListAsync();

        foreach (var message in unread)
        {
            message.IsRead = true;
        }

        if (unread.Count > 0)
        {
            await _db.SaveChangesAsync();

            // Meta only needs the newest message id to clear read state up through it on the
            // customer's side — no need to call once per message. Best-effort: a customer's own
            // WhatsApp app not showing blue ticks isn't worth failing this request over.
            var lastWhatsappMessageId = unread
                .Where(m => !string.IsNullOrEmpty(m.WhatsappMessageId))
                .OrderByDescending(m => m.TimeSent)
                .Select(m => m.WhatsappMessageId)
                .FirstOrDefault();

            if (lastWhatsappMessageId is not null)
            {
                await _whatsAppService.MarkMessageAsReadAsync(lastWhatsappMessageId);
            }
        }

        return Ok();
    }

    [HttpPost]
    [Authorize(Policy = "chat.view")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBlock(int chatId)
    {
        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat is null)
        {
            return NotFound();
        }

        chat.IsBlocked = !chat.IsBlocked;
        chat.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Json(new { isBlocked = chat.IsBlocked });
    }

    [HttpGet]
    [Authorize(Policy = "chat.view,chat.read_only")]
    public async Task<IActionResult> GetCannedReplies()
    {
        var replies = await _db.CannedReplies.AsNoTracking()
            .Where(r => r.IsPublic)
            .OrderBy(r => r.Title)
            .Select(r => new CannedReplyDto(r.Id, r.Title, r.Description))
            .ToListAsync();

        return Json(replies);
    }

    /// <summary>
    /// The "Add to Contact" quick action from an open chat — for a number that messaged in but
    /// was never (or is no longer) a saved Contact. Reuses whatever number already matches by
    /// phone rather than creating a duplicate, same guard SendTemplate's Contacts-page equivalent
    /// already relies on elsewhere.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "contact.create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddContact(int chatId, string firstName, string? lastName, ContactType type, int statusId, int sourceId)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            return BadRequest("First name is required.");
        }

        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat is null)
        {
            return NotFound();
        }

        var normalized = PhoneNumberNormalizer.Normalize(chat.ReceiverId);
        var existing = await _db.Contacts.FirstOrDefaultAsync(c => c.Phone == chat.ReceiverId || c.Phone == normalized || c.Phone == "+" + normalized);
        if (existing is not null)
        {
            return Json(new { contactId = existing.Id, contactType = existing.Type.ToString(), contactName = $"{existing.FirstName} {existing.LastName}".Trim() });
        }

        var contact = new Contact
        {
            WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
            FirstName = firstName.Trim(),
            LastName = (lastName ?? string.Empty).Trim(),
            Phone = normalized,
            Type = type,
            StatusId = statusId,
            SourceId = sourceId,
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();

        return Json(new { contactId = contact.Id, contactType = contact.Type.ToString(), contactName = $"{contact.FirstName} {contact.LastName}".Trim() });
    }

    /// <summary>
    /// Send-Template-from-the-open-chat — the same underlying send as Contacts' SendTemplate quick
    /// action, but returning a ChatMessageDto so the caller can append it to the open thread
    /// in-place instead of a full-page redirect. The natural way to re-open a closed 24-hour
    /// window, since Meta only accepts template sends once it's expired.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "chat.view")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTemplate(int chatId, string templateId, string? headerParam, List<string>? bodyParams)
    {
        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat is null)
        {
            return NotFound();
        }

        if (chat.IsBlocked)
        {
            return BadRequest("This contact is blocked — unblock them first to send a message.");
        }

        var template = await _db.WhatsappTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MetaTemplateId == templateId && t.Status == TemplateStatus.Approved);
        if (template is null)
        {
            return BadRequest("Choose an approved template.");
        }

        var request = new TemplateSendRequest
        {
            TemplateName = template.TemplateName,
            Language = template.Language,
            HeaderFormat = template.HeaderFormat,
            HeaderText = string.Equals(template.HeaderFormat, "TEXT", StringComparison.OrdinalIgnoreCase) ? headerParam : null,
            HeaderMediaUrl = string.Equals(template.HeaderFormat, "TEXT", StringComparison.OrdinalIgnoreCase) ? null : headerParam,
            BodyParams = (bodyParams ?? []).Take(template.BodyParamsCount).ToList(),
        };

        var result = await _whatsAppService.SendTemplateMessageAsync(chat.ReceiverId, request);
        if (!result.Success)
        {
            return BadRequest(result.ErrorMessage);
        }

        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var rendered = TemplateMessageRenderer.ForChatMessage(template, request);

        var message = new ChatMessage
        {
            ChatId = chat.Id,
            SenderId = chat.WaNoId ?? "agent",
            Message = rendered.DisplayText,
            MessageType = rendered.MediaMessageType ?? "text",
            Url = rendered.MediaUrl,
            WhatsappMessageId = result.Data,
            StaffId = userId,
            Status = MessageDeliveryStatus.Sent,
            TimeSent = DateTime.UtcNow,
            TemplateName = template.TemplateName,
            IsRead = true,
        };
        _db.ChatMessages.Add(message);

        chat.LastMessage = ChatPreviewText.ForMedia(message.MessageType, message.Message);
        chat.LastMessageTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dto = ToDto(message, chat);
        await _hubContext.Clients.All.SendAsync("ReceiveMessage", chatId, dto);

        return Json(dto);
    }

    /// <summary>True while a free-form (non-template) reply is still allowed — Meta's 24-hour
    /// customer service window, measured from this chat's last actually-inbound message.</summary>
    private async Task<bool> IsWindowOpenAsync(Chat chat)
    {
        var lastInbound = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ChatId == chat.Id && m.StaffId == null && m.SenderId == chat.ReceiverId)
            .OrderByDescending(m => m.TimeSent)
            .Select(m => (DateTime?)m.TimeSent)
            .FirstOrDefaultAsync();

        return lastInbound is not null && DateTime.UtcNow - lastInbound < TimeSpan.FromHours(24);
    }

    private static ChatMessageDto ToDto(ChatMessage message, Chat chat) => new(
        message.Id,
        message.SenderId,
        message.Message,
        message.MessageType,
        message.TimeSent,
        IsOutbound: message.StaffId is not null || message.SenderId != chat.ReceiverId,
        message.Status.ToString(),
        message.Url);
}
