using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Authorization;
using Msgifly.Web.Data;
using Msgifly.Web.Services.ApiKeys;

namespace Msgifly.Web.Controllers.Api.V1;

[ApiController]
[Route("api/v1/conversations")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public class ConversationsController : ControllerBase
{
    private const int MessagePageSize = 50;
    private readonly ApplicationDbContext _db;

    public ConversationsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        if (!User.HasApiScope(ApiScopes.ConversationsRead))
        {
            return Forbid();
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(page, 1);

        var query = _db.Chats.AsNoTracking().OrderByDescending(c => c.LastMessageTime);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return Ok(new
        {
            data = items.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                phone = c.ReceiverId,
                last_message = c.LastMessage,
                last_message_time = c.LastMessageTime,
                is_bots_stopped = c.IsBotsStopped,
            }),
            meta = new { page, page_size = pageSize, total },
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (!User.HasApiScope(ApiScopes.ConversationsRead))
        {
            return Forbid();
        }

        var chat = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (chat is null)
        {
            return NotFound(new { error = "not_found", message = "Conversation not found." });
        }

        return Ok(new
        {
            data = new
            {
                id = chat.Id,
                name = chat.Name,
                phone = chat.ReceiverId,
                last_message = chat.LastMessage,
                last_message_time = chat.LastMessageTime,
                is_bots_stopped = chat.IsBotsStopped,
            },
        });
    }

    [HttpGet("{id:int}/messages")]
    public async Task<IActionResult> Messages(int id, [FromQuery] int? beforeId)
    {
        if (!User.HasApiScope(ApiScopes.MessagesRead))
        {
            return Forbid();
        }

        var chat = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (chat is null)
        {
            return NotFound(new { error = "not_found", message = "Conversation not found." });
        }

        var query = _db.ChatMessages.AsNoTracking().Where(m => m.ChatId == id);
        if (beforeId is not null)
        {
            query = query.Where(m => m.Id < beforeId);
        }

        var messages = await query.OrderByDescending(m => m.Id).Take(MessagePageSize).ToListAsync();
        messages.Reverse();

        return Ok(new
        {
            data = messages.Select(m => new
            {
                id = m.Id,
                sender_id = m.SenderId,
                message = m.Message,
                message_type = m.MessageType,
                url = m.Url,
                status = m.Status.ToString(),
                whatsapp_message_id = m.WhatsappMessageId,
                is_outbound = m.StaffId is not null || m.SenderId != chat.ReceiverId,
                time_sent = m.TimeSent,
            }),
        });
    }
}
