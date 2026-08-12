using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>A no-code trigger -> steps workflow, the .NET rewrite's answer to the original's
/// simple keyword-trigger bots — but supporting waits, conditional branches, and webhooks.</summary>
public class Automation
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public AutomationTriggerType TriggerType { get; set; }

    /// <summary>Trigger-specific config, e.g. {"keywords":["price"],"matchType":"contains"} for KeywordMatch.</summary>
    public string TriggerConfigJson { get; set; } = "{}";

    public bool IsActive { get; set; }
    public int ExecutionCount { get; set; }
    public DateTime? LastExecutedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AutomationStep> Steps { get; set; } = new List<AutomationStep>();
}
