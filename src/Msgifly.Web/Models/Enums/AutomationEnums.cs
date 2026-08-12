namespace Msgifly.Web.Models.Enums;

/// <summary>What starts an automation run. Kept intentionally smaller than a full CRM's trigger
/// vocabulary (no tags/pipeline/conversation-assignment yet — those don't exist in Msgifly).</summary>
public enum AutomationTriggerType
{
    /// <summary>Any inbound WhatsApp message.</summary>
    InboundMessage = 0,

    /// <summary>The first message ever received from a given contact.</summary>
    FirstInboundMessage = 1,

    /// <summary>Inbound message text matches one of the configured keywords.</summary>
    KeywordMatch = 2,

    /// <summary>A new Contact row was created (manually or via auto-lead-from-WhatsApp).</summary>
    NewContactCreated = 3,

    /// <summary>Customer tapped a specific quick-reply button or list row.</summary>
    InteractiveReply = 4,

    /// <summary>A new lead came in from a connected Facebook Lead Ads (Instant Forms) form — a
    /// more specific case of NewContactCreated, broken out as its own trigger so "follow up
    /// instantly on ad leads" is a one-click automation to set up rather than something that
    /// also silently fires for every manually-added or WhatsApp-inbound contact too.</summary>
    FacebookLeadReceived = 5,
}

public enum AutomationStepType
{
    SendMessage = 0,
    SendTemplate = 1,
    SendButtons = 2,
    Wait = 3,

    /// <summary>Branches into Yes/No child steps based on ConditionSubject.</summary>
    Condition = 4,

    UpdateContactField = 5,
    SendWebhook = 6,

    /// <summary>No-op terminal marker — ends this branch early.</summary>
    Stop = 7,
}

public enum ConditionSubject
{
    /// <summary>Case-insensitive substring check against the triggering message text.</summary>
    MessageContent = 0,

    /// <summary>Compares a Contact field (Operand = field name) to Value.</summary>
    ContactField = 1,

    /// <summary>Operand is "HH:mm-HH:mm"; true if the current server time falls in that window.</summary>
    TimeOfDay = 2,
}

public enum AutomationLogStatus
{
    Success = 0,
    Partial = 1,
    Failed = 2,
}
