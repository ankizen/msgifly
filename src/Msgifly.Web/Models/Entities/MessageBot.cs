using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Free-text/interactive auto-reply bot (as opposed to a template-based TemplateBot).</summary>
public class MessageBot
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ContactType RelType { get; set; } = ContactType.Lead;
    public string ReplyText { get; set; } = string.Empty;
    public ReplyType ReplyType { get; set; } = ReplyType.Contains;

    /// <summary>JSON array of trigger keywords.</summary>
    public string? TriggersJson { get; set; }

    public string? HeaderText { get; set; }
    public string? FooterText { get; set; }

    // Up to 3 quick-reply buttons...
    public string? Button1Text { get; set; }
    public string? Button1Id { get; set; }
    public string? Button2Text { get; set; }
    public string? Button2Id { get; set; }
    public string? Button3Text { get; set; }
    public string? Button3Id { get; set; }

    // ...or a single call-to-action URL button.
    public string? CtaButtonText { get; set; }
    public string? CtaButtonUrl { get; set; }

    public int? AddedFromId { get; set; }
    public bool IsActive { get; set; } = true;
    public int SendingCount { get; set; }
    public string? FileName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
