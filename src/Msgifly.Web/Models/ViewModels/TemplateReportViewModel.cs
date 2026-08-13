namespace Msgifly.Web.Models.ViewModels;

/// <summary>Aggregates sent/delivered/read/failed/clicked counts for one template across every
/// way it can be sent — Campaign (via CampaignDetail, joined on Campaign.TemplateId) and
/// everything else (ChatMessage.TemplateName: single quick-sends, bot replies, automations, the
/// public API) — since only counting Campaign sends would silently under-report anything sent
/// outside a campaign.</summary>
public class TemplateReportViewModel
{
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;

    public int SentCount { get; set; }
    public int DeliveredCount { get; set; }
    public int ReadCount { get; set; }
    public int FailedCount { get; set; }
    public int ClickedCount { get; set; }

    public List<TemplateFailureReason> FailureReasons { get; set; } = [];
}

public record TemplateFailureReason(string Reason, int Count);
