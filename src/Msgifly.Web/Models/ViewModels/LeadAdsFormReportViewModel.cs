namespace Msgifly.Web.Models.ViewModels;

/// <summary>
/// The ad-spend-to-engagement funnel for one Lead Ads form — leads imported, template messages
/// sent to them, and how many were delivered/read/clicked. Answers "is this form's ad spend
/// actually working," which neither the per-form import counts on LeadAdsController.Index nor the
/// per-template funnel on TemplatesController.Report show on their own (the former doesn't know
/// about outbound messages at all; the latter has no per-form breakdown).
/// </summary>
public class LeadAdsFormReportViewModel
{
    public string FormId { get; set; } = string.Empty;
    public string FormName { get; set; } = string.Empty;

    public int LeadsImported { get; set; }
    public int TemplatesSent { get; set; }
    public int DeliveredCount { get; set; }
    public int ReadCount { get; set; }
    public int ClickedCount { get; set; }
    public int FailedCount { get; set; }

    public List<LeadAdsFormTemplateStat> ByTemplate { get; set; } = [];
}

public record LeadAdsFormTemplateStat(string TemplateName, int Sent, int Clicked);
