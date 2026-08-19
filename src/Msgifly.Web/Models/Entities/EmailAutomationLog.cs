using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Audit trail of one EmailAutomation run against one subscriber — mirrors AutomationLog.</summary>
public class EmailAutomationLog
{
    public int Id { get; set; }

    public int AutomationId { get; set; }
    public EmailAutomation Automation { get; set; } = null!;

    /// <summary>Contact IS the email subscriber — no separate subscriber table.</summary>
    public int SubscriberId { get; set; }
    public Contact Subscriber { get; set; } = null!;

    public EmailAutomationLogStatus Status { get; set; }

    /// <summary>JSON array of {stepId, stepType, status, detail} — appended to as each step runs.</summary>
    public string ResultJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
