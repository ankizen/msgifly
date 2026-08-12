using System.Text.RegularExpressions;
using Msgifly.Web.Models.Entities;

namespace Msgifly.Web.Services.WhatsApp;

/// <summary>
/// Turns a WhatsappTemplate + the parameter values used for one send into the text/media a
/// recipient actually saw — used everywhere a template send needs to be recorded as a normal
/// Chat message (inbox bubbles, bot replies, the public API) instead of an opaque
/// "[Template: name]" placeholder that doesn't tell the person reading the conversation what was
/// actually said.
/// </summary>
public static class TemplateMessageRenderer
{
    public record Rendered(string DisplayText, string? MediaMessageType, string? MediaUrl);

    /// <summary>Substitutes {{1}}, {{2}}… with positional values; a placeholder with no matching
    /// value is left as-is rather than blanked, so a misconfigured send is still noticeable.</summary>
    public static string? RenderText(string? templateText, IReadOnlyList<string> paramValues)
    {
        if (string.IsNullOrEmpty(templateText))
        {
            return templateText;
        }

        return Regex.Replace(templateText, @"\{\{(\d+)\}\}", match =>
        {
            var index = int.Parse(match.Groups[1].Value) - 1;
            return index >= 0 && index < paramValues.Count ? paramValues[index] : match.Value;
        });
    }

    /// <summary>Full display form for a Chat bubble: header + body + footer with params filled
    /// in. When the header is media, the media type/url are returned separately so the caller can
    /// store them on MessageType/Url — the Chat view already knows how to render an image/video/
    /// document bubble, so a template with a media header renders exactly like a normal media
    /// message with the body text as its caption, matching what the recipient saw.</summary>
    public static Rendered ForChatMessage(WhatsappTemplate template, TemplateSendRequest request)
    {
        var isTextHeader = string.Equals(template.HeaderFormat, "TEXT", StringComparison.OrdinalIgnoreCase);
        var isMediaHeader = template.HeaderFormat is "IMAGE" or "VIDEO" or "DOCUMENT";

        var parts = new List<string>();
        if (isTextHeader && !string.IsNullOrWhiteSpace(template.HeaderText))
        {
            var headerValues = string.IsNullOrEmpty(request.HeaderText) ? [] : new List<string> { request.HeaderText };
            parts.Add(RenderText(template.HeaderText, headerValues) ?? string.Empty);
        }

        parts.Add(RenderText(template.BodyText, request.BodyParams) ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(template.FooterText))
        {
            parts.Add(template.FooterText);
        }

        var displayText = string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        var mediaUrl = isMediaHeader ? (request.HeaderMediaUrl ?? template.HeaderMediaUrl) : null;
        var mediaMessageType = isMediaHeader && !string.IsNullOrEmpty(mediaUrl) ? template.HeaderFormat!.ToLowerInvariant() : null;

        return new Rendered(displayText, mediaMessageType, mediaUrl);
    }
}
