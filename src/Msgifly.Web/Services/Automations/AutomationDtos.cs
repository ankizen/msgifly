namespace Msgifly.Web.Services.Automations;

/// <summary>Everything an in-flight automation run carries between steps.</summary>
public class AutomationContext
{
    /// <summary>Raw inbound message text — drives KeywordMatch trigger matching and the message_content condition.</summary>
    public string? MessageText { get; set; }

    public int? ChatId { get; set; }

    /// <summary>Button/list-row id the customer tapped, for the InteractiveReply trigger.</summary>
    public string? InteractiveReplyId { get; set; }

    /// <summary>Arbitrary accumulated variables, readable in step text via {{vars.x}}.</summary>
    public Dictionary<string, string> Vars { get; set; } = [];
}

public class KeywordMatchTriggerConfig
{
    public List<string> Keywords { get; set; } = [];

    /// <summary>contains | exact | word.</summary>
    public string MatchType { get; set; } = "contains";

    public bool CaseSensitive { get; set; }
}

public class InteractiveReplyTriggerConfig
{
    public List<string> ReplyIds { get; set; } = [];
}

public class SendMessageStepConfig
{
    public string Text { get; set; } = string.Empty;
}

public class SendTemplateStepConfig
{
    public string TemplateName { get; set; } = string.Empty;
    public string Language { get; set; } = "en_US";
    public List<string> BodyParams { get; set; } = [];
}

public record AutomationButtonConfig(string Id, string Title);

public class SendButtonsStepConfig
{
    public string BodyText { get; set; } = string.Empty;
    public List<AutomationButtonConfig> Buttons { get; set; } = [];
}

public class WaitStepConfig
{
    public int Amount { get; set; } = 1;

    /// <summary>minutes | hours | days.</summary>
    public string Unit { get; set; } = "minutes";
}

public class ConditionStepConfig
{
    /// <summary>MessageContent | ContactField | TimeOfDay.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>ContactField: the field name. TimeOfDay: "HH:mm-HH:mm".</summary>
    public string? Operand { get; set; }

    /// <summary>MessageContent: the substring to look for. ContactField: the expected value.</summary>
    public string? Value { get; set; }
}

public class UpdateContactFieldStepConfig
{
    public string Field { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class SendWebhookStepConfig
{
    public string Url { get; set; } = string.Empty;
    public string? BodyTemplate { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

public record AutomationStepResult(int? StepId, string StepType, string Status, string Detail);
