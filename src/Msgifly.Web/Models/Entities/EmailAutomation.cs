using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>The automation container. No Channel field needed — this table is Email-only by
/// construction, structurally modeled on (but fully independent of) WhatsApp's Automation.</summary>
public class EmailAutomation
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public EmailAutomationTriggerType TriggerType { get; set; }

    /// <summary>Trigger-specific config, e.g. {"listId":..} / {"tagId":..}.</summary>
    public string TriggerConfigJson { get; set; } = "{}";

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<EmailAutomationStep> Steps { get; set; } = new List<EmailAutomationStep>();
}
