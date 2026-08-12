using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Auto-reply bot that wraps an approved WhatsApp template (as opposed to free-text MessageBot).</summary>
public class TemplateBot
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ContactType RelType { get; set; } = ContactType.Lead;

    /// <summary>Meta template id (WhatsappTemplate.MetaTemplateId).</summary>
    public string? TemplateId { get; set; }

    public string? HeaderParamsJson { get; set; }
    public string? BodyParamsJson { get; set; }
    public string? FooterParamsJson { get; set; }
    public string? FileName { get; set; }

    public string? TriggersJson { get; set; }
    public ReplyType ReplyType { get; set; } = ReplyType.Contains;
    public bool IsActive { get; set; } = true;
    public int SendingCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
