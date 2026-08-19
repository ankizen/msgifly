namespace Msgifly.Web.Models.Enums;

/// <summary>What starts an EmailAutomation run. Independent of (and smaller than) WhatsApp's
/// AutomationTriggerType — this stack only reacts to subscriber/list/tag events.</summary>
public enum EmailAutomationTriggerType
{
    SubscriberAdded = 0,
    TagApplied = 1,
    ListApplied = 2,
}

public enum EmailAutomationStepType
{
    SendEmail = 0,
    Wait = 1,

    /// <summary>Branches into Yes/No child steps based on the step's ConfigJson.</summary>
    Condition = 2,

    AddTag = 3,
    RemoveTag = 4,
    AddToList = 5,
    RemoveFromList = 6,
    UpdateSubscriberField = 7,
    Webhook = 8,

    /// <summary>No-op terminal marker — ends this branch early.</summary>
    Stop = 9,
}

public enum EmailAutomationLogStatus
{
    Success = 0,
    Failed = 1,
    Partial = 2,
}

public enum EmailSequenceStatus
{
    Draft = 0,
    Active = 1,
    Paused = 2,
}

public enum EmailSequenceSubscriberStatus
{
    Active = 0,
    Completed = 1,
    Cancelled = 2,
}
