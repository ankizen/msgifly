namespace Msgifly.Web.Models.ViewModels;

/// <summary>Run-level and per-step analytics for one automation — how many contacts have gone
/// through it, and for each step in tree order, how far it got (SendTemplate: sent/delivered/
/// read/clicked/failed with reasons; Condition: Yes/No split; everything else: reached/succeeded/
/// failed). Scoped to runs of THIS automation specifically (via AutomationLog), unlike the
/// Templates Report page which aggregates a template across every automation/campaign/quick-send
/// that ever used it.</summary>
public class AutomationReportViewModel
{
    public int AutomationId { get; set; }
    public string AutomationName { get; set; } = string.Empty;

    public int TotalRuns { get; set; }
    public int CompletedRuns { get; set; }
    public int WaitingRuns { get; set; }
    public int FailedRuns { get; set; }

    public List<AutomationStepReport> Steps { get; set; } = [];
}

public record AutomationFailureReason(string Reason, int Count);

public class AutomationStepReport
{
    public int StepId { get; set; }
    public string StepType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>"" for a root-level step, "Yes"/"No" under a Condition — drives indentation so the
    /// report reads in the same shape as the canvas.</summary>
    public string Branch { get; set; } = string.Empty;
    public int Depth { get; set; }

    public int ReachedCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<AutomationFailureReason> FailureReasons { get; set; } = [];

    public bool IsSendTemplate { get; set; }
    public int DeliveredCount { get; set; }
    public int ReadCount { get; set; }
    public int ClickedCount { get; set; }

    public bool IsCondition { get; set; }
    public int YesCount { get; set; }
    public int NoCount { get; set; }
}
