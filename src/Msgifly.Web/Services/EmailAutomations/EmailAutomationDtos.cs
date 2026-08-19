using System.Text.Json;

namespace Msgifly.Web.Services.EmailAutomations;

/// <summary>The step-tree wire shape posted by the email-automation-builder canvas — independent
/// copy of AutomationStepNode's exact shape (Type/Config/Yes/No), no shared type.</summary>
public class EmailAutomationStepNode
{
    public string Type { get; set; } = string.Empty;
    public JsonElement Config { get; set; }
    public List<EmailAutomationStepNode>? Yes { get; set; }
    public List<EmailAutomationStepNode>? No { get; set; }
}

/// <summary>Everything an in-flight EmailAutomation run carries between steps. Independent copy of
/// AutomationContext's shape — no shared type between the two stacks.</summary>
public class EmailAutomationContext
{
    /// <summary>The EmailList involved, for SubscriberAdded/ListApplied trigger matching.</summary>
    public int? ListId { get; set; }

    /// <summary>The EmailTag involved, for TagApplied trigger matching.</summary>
    public int? TagId { get; set; }

    /// <summary>Arbitrary accumulated variables, readable in step text via {{vars.x}}.</summary>
    public Dictionary<string, string> Vars { get; set; } = [];
}

/// <summary>Null ListId means "any list" (SubscriberAdded fires on any list membership); a
/// non-null ListId scopes the trigger to one specific list. Also reused for ListApplied.</summary>
public class ListScopedTriggerConfig
{
    public int? ListId { get; set; }
}

public class TagAppliedTriggerConfig
{
    public int? TagId { get; set; }
}

public class SendEmailStepConfig
{
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
}

public class WaitStepConfig
{
    public int Amount { get; set; } = 1;

    /// <summary>minutes | hours | days.</summary>
    public string Unit { get; set; } = "minutes";
}

public class EmailConditionStepConfig
{
    /// <summary>SubscriberField | HasTag | HasList.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>SubscriberField: the field name. HasTag/HasList: the tag/list id (as a string).</summary>
    public string? Operand { get; set; }

    /// <summary>SubscriberField: the expected value.</summary>
    public string? Value { get; set; }
}

public class TagStepConfig
{
    public int TagId { get; set; }
}

public class ListStepConfig
{
    public int ListId { get; set; }
}

public class UpdateSubscriberFieldStepConfig
{
    public string Field { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class WebhookStepConfig
{
    public string Url { get; set; } = string.Empty;
    public string? BodyTemplate { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

public record EmailAutomationStepResult(int? StepId, string StepType, string Status, string Detail);
