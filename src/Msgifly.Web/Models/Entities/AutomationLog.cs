using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Audit trail of one automation run — surfaced on the automation's Logs screen so a
/// failure ("webhook returned 500", "send_template needs template_name") is diagnosable.</summary>
public class AutomationLog
{
    public int Id { get; set; }

    public int AutomationId { get; set; }
    public Automation Automation { get; set; } = null!;

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public string TriggerEvent { get; set; } = string.Empty;

    /// <summary>JSON array of {stepId, stepType, status, detail} — appended to as each step runs.</summary>
    public string StepsExecutedJson { get; set; } = "[]";

    public AutomationLogStatus Status { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
