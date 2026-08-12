namespace Msgifly.Web.Models.Entities;

/// <summary>A WhatsApp conversation thread, keyed by the contact's phone number.</summary>
public class Chat
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The contact's WhatsApp number — the thread key (unique per-Workspace, not globally: the same customer number can message different businesses' numbers, each its own Chat).</summary>
    public string ReceiverId { get; set; } = string.Empty;

    public string? LastMessage { get; set; }
    public DateTime? LastMessageTime { get; set; }

    /// <summary>Which of our business numbers this thread belongs to (multi-number support).</summary>
    public string? WaNo { get; set; }
    public string? WaNoId { get; set; }

    public int? AssignedAgentId { get; set; }
    public ApplicationUser? AssignedAgent { get; set; }

    public bool IsAiChat { get; set; }
    public bool IsBotsStopped { get; set; }
    public DateTime? BotStoppedTime { get; set; }

    /// <summary>Msgifly-side block — the Cloud API has no platform-level "block this number" of
    /// its own, so this stops us: bots/automations skip a blocked chat's inbound messages, and
    /// the composer/send endpoints refuse outbound ones, same idea as IsBotsStopped but stronger
    /// and manually toggled rather than keyword- or time-triggered.</summary>
    public bool IsBlocked { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
