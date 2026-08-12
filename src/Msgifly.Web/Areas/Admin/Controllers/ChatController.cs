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
using Msgifly.Web.Services.WhatsApp;

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

    public ChatController(ApplicationDbContext db, IWhatsAppService whatsAppService, IHubContext<ChatHub> hubContext, IWebHostEnvironment environment)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _hubContext = hubContext;
        _environment = environment;
    }

    [Authorize(Policy = "chat.view,chat.read_only")]
    public IActionResult Index() => View();

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
        var contactTypes = await _db.Contacts.AsNoTracking()
            .Where(c => receiverIds.Contains(c.Phone))
            .GroupBy(c => c.Phone)
            .Select(g => new { Phone = g.Key, Type = g.First().Type })
            .ToDictionaryAsync(x => x.Phone, x => x.Type);

        var unreadCounts = await _db.ChatMessages.AsNoTracking()
            .Where(m => chats.Select(c => c.Id).Contains(m.ChatId) && !m.IsRead && m.StaffId == null)
            .GroupBy(m => m.ChatId)
            .Select(g => new { ChatId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ChatId, x => x.Count);

        var result = chats.Select(c => new ChatSummaryDto(
            c.Id,
            c.Name,
            c.ReceiverId,
            c.LastMessage,
            c.LastMessageTime,
            unreadCounts.GetValueOrDefault(c.Id),
            contactTypes.TryGetValue(c.ReceiverId, out var t) ? t.ToString() : "Unknown",
            c.IsBotsStopped,
            c.IsBlocked));

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
            Message = string.IsNullOrWhiteSpace(caption) ? $"[{mediaType}] {file.FileName}" : caption,
            MessageType = mediaType,
            Url = $"/uploads/chat/{storedFileName}",
            WhatsappMessageId = sendResult.Data,
            StaffId = userId,
            Status = MessageDeliveryStatus.Sent,
            TimeSent = DateTime.UtcNow,
            IsRead = true,
        };
        _db.ChatMessages.Add(message);

        chat.LastMessage = message.Message;
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
