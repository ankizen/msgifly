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
    string? Url = null);

public record CannedReplyDto(int Id, string Title, string Description);
