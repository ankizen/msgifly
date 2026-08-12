namespace Msgifly.Web.Models.ViewModels;

public record ChatSummaryDto(
    int Id,
    string Name,
    string ReceiverId,
    string? LastMessage,
    DateTime? LastMessageTime,
    int UnreadCount,
    string ContactType,
    bool IsBotsStopped);

public record ChatMessageDto(
    int Id,
    string SenderId,
    string Message,
    string? MessageType,
    DateTime TimeSent,
    bool IsOutbound,
    string Status);

public record CannedReplyDto(int Id, string Title, string Description);
