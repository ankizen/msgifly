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
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IHubContext<ChatHub> _hubContext;

    public ChatController(ApplicationDbContext db, IWhatsAppService whatsAppService, IHubContext<ChatHub> hubContext)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _hubContext = hubContext;
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

        var contactTypes = await _db.Contacts.AsNoTracking()
            .Where(c => receiverIds.Contains(c.Phone))
            .ToDictionaryAsync(c => c.Phone, c => c.Type);

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
            c.IsBotsStopped));

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
        message.Status.ToString());
}
