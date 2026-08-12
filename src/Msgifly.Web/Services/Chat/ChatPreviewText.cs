namespace Msgifly.Web.Services.Chat;

/// <summary>
/// The short label shown in the conversation list for a message that has no caption text of its
/// own (a bare media send) — mirrors how WhatsApp itself shows "Photo"/"Video" etc. in its chat
/// list rather than a filename or a raw "[image]" tag.
/// </summary>
public static class ChatPreviewText
{
    public static string ForMedia(string messageType, string? caption)
    {
        if (!string.IsNullOrWhiteSpace(caption))
        {
            return caption;
        }

        return messageType switch
        {
            "image" => "📷 Photo",
            "video" => "🎥 Video",
            "audio" => "🎵 Audio",
            "sticker" => "Sticker",
            "document" => "📄 Document",
            _ => messageType,
        };
    }
}
