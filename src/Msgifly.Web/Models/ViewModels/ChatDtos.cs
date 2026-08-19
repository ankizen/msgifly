namespace Msgifly.Web.Models.ViewModels;

public record ChatSummaryDto(
    int Id,
    string Name,
    string ReceiverId,
    string? LastMessage,
    DateTime? LastMessageTime,
    int UnreadCount,
    string ContactType,
    bool IsBotsStopped,
    bool IsBlocked,
    /// <summary>Meta's 24-hour customer service window — true while a free-form (non-template) reply is still allowed, based on this chat's last INBOUND message. False once it's closed, meaning only a template message can re-open the conversation.</summary>
    bool WindowOpen);

public record ChatMessageDto(
    int Id,
    string SenderId,
    string Message,
    string? MessageType,
    DateTime TimeSent,
    bool IsOutbound,
    string Status,
    string? Url = null,
    /// <summary>Set only for a template send whose template still exists locally — looked up live
    /// by TemplateName rather than stored per-message, so it reflects the template's current
    /// footer/buttons even for messages sent before this field existed.</summary>
    string? FooterText = null,
    string? ButtonsJson = null,
    /// <summary>Set when this message is a quoted reply to another one — resolved live from
    /// RefMessageId rather than duplicating the quoted content per-message.</summary>
    RepliedToPreview? RepliedTo = null,
    string? ReactionEmoji = null,
    bool IsPinned = false);

/// <summary>A short, ready-to-render preview of the message a reply is quoting — Id lets the
/// client scroll to/highlight the original if it wants to, Preview is already resolved to a
/// caption/media-label via ChatPreviewText, matching how the conversation list itself previews.</summary>
public record RepliedToPreview(int Id, string Preview, string? MessageType);

public record CannedReplyDto(int Id, string Title, string Description);
