using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>
/// One step in an EmailAutomation's tree. Root-level steps have ParentStepId = null; a Condition
/// step's Yes/No children set ParentStepId to the condition's own Id and Branch to "Yes"/"No".
/// Position orders siblings within their scope (root, or a specific parent+branch). Mirrors
/// AutomationStep's exact shape as an independent table.
/// </summary>
public class EmailAutomationStep
{
    public int Id { get; set; }

    public int AutomationId { get; set; }
    public EmailAutomation Automation { get; set; } = null!;

    public int? ParentStepId { get; set; }
    public EmailAutomationStep? ParentStep { get; set; }

    /// <summary>"Yes" | "No" | null (null for root-level steps).</summary>
    public string? Branch { get; set; }

    public EmailAutomationStepType StepType { get; set; }

    /// <summary>Step-specific config — shape depends on StepType (see EmailAutomationEngine).</summary>
    public string StepConfigJson { get; set; } = "{}";

    public int Position { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
