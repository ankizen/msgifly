using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

public class ChatMessage
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public Chat Chat { get; set; } = null!;

    /// <summary>Phone number (inbound) or business number (outbound) that originated this message.</summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>Attachment URL/local storage path, if any.</summary>
    public string? Url { get; set; }

    public string Message { get; set; } = string.Empty;
    public MessageDeliveryStatus Status { get; set; } = MessageDeliveryStatus.Pending;

    /// <summary>Meta's status-callback failure detail ("{code}: {title}"), when Status is Failed.</summary>
    public string? StatusDetail { get; set; }

    public DateTime TimeSent { get; set; } = DateTime.UtcNow;

    /// <summary>WhatsApp message id (wamid...) — dedupe key for inbound webhook processing.</summary>
    public string? WhatsappMessageId { get; set; }

    /// <summary>Agent user id, if this was sent by a human (not a bot/campaign).</summary>
    public int? StaffId { get; set; }

    public string? MessageType { get; set; }
    public bool IsRead { get; set; }

    /// <summary>WhatsApp message id this is a reply/quote to, if any.</summary>
    public string? RefMessageId { get; set; }

    /// <summary>Set only for outbound template sends (single quick-send, bot reply, or automation
    /// step) — null for plain text/media messages. Campaign sends are attributed via
    /// Campaign.TemplateId instead, since they're not tracked as ChatMessage rows at all.</summary>
    public string? TemplateName { get; set; }

    // Per-stage delivery timestamps — Status alone only ever reflects the *latest* stage reached,
    // so these are what actually let a report show sent→delivered→read timing/funnel instead of
    // just a final snapshot.
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? FailedAt { get; set; }

    /// <summary>True if this outbound template message's quick-reply button was tapped.</summary>
    public bool Clicked { get; set; }
    public string? ClickedButtonText { get; set; }

    /// <summary>The single emoji currently reacted onto this message (from either side — customer
    /// or agent), or null if none/removed. WhatsApp allows only one reaction per person per
    /// message, and this inbox shows one combined state per message rather than per-person.</summary>
    public string? ReactionEmoji { get; set; }

    /// <summary>Purely a local organizing feature — WhatsApp's Cloud API has no "pin" concept at
    /// all (real WhatsApp pinning is client-side only, never sent over the wire), so this never
    /// calls out to Meta. One pinned message per chat; pinning a new one unpins whatever was
    /// pinned before it, matching a simple single pinned-banner UI.</summary>
    public bool IsPinned { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
