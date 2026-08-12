using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>
/// One step in an automation's tree. Root-level steps have ParentStepId = null; a Condition
/// step's Yes/No children set ParentStepId to the condition's own Id and Branch to "Yes"/"No".
/// Position orders siblings within their scope (root, or a specific parent+branch).
/// </summary>
public class AutomationStep
{
    public int Id { get; set; }

    public int AutomationId { get; set; }
    public Automation Automation { get; set; } = null!;

    public int? ParentStepId { get; set; }
    public AutomationStep? ParentStep { get; set; }

    /// <summary>"Yes" | "No" | null (null for root-level steps).</summary>
    public string? Branch { get; set; }

    public AutomationStepType StepType { get; set; }

    /// <summary>Step-specific config — shape depends on StepType (see AutomationEngine).</summary>
    public string StepConfigJson { get; set; } = "{}";

    public int Position { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
