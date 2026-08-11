using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.ViewModels;

public class CampaignParamInput
{
    public ParamSourceType Source { get; set; } = ParamSourceType.StaticText;
    public string? StaticValue { get; set; }
}

public class CampaignFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Recipient type")]
    public ContactType RelType { get; set; } = ContactType.Lead;

    [Required]
    [Display(Name = "Template")]
    public string TemplateId { get; set; } = string.Empty;

    public bool SendNow { get; set; } = true;
    public DateTime? ScheduledSendTime { get; set; }

    public bool SelectAll { get; set; } = true;
    public int? FilterStatusId { get; set; }
    public int? FilterSourceId { get; set; }
    public List<int> SelectedContactIds { get; set; } = [];

    public string? HeaderMediaUrl { get; set; }

    public const int MaxBodyParams = 6;

    // Fixed-size slots (see the Save view) — only the first N per the selected template's
    // param counts are actually rendered/used; keeps model binding simple (indexed arrays)
    // without needing full client-side dynamic form-array JS.
    public CampaignParamInput[] HeaderParams { get; set; } = new CampaignParamInput[1];
    public CampaignParamInput[] BodyParams { get; set; } = new CampaignParamInput[MaxBodyParams];
    public CampaignParamInput[] FooterParams { get; set; } = new CampaignParamInput[1];

    // View-only lookups, populated by the controller.
    public List<TemplateOption> TemplateOptions { get; set; } = [];
    public List<SelectListItem> StatusOptions { get; set; } = [];
    public List<SelectListItem> SourceOptions { get; set; } = [];
    public List<ContactOption> ContactOptions { get; set; } = [];
}

public record TemplateOption(string TemplateId, string Name, string? HeaderFormat, int HeaderParamsCount, int BodyParamsCount, int FooterParamsCount);

public record ContactOption(int Id, string Label);

public class CampaignListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ContactType RelType { get; set; }
    public string? TemplateName { get; set; }
    public bool IsSent { get; set; }
    public bool PauseCampaign { get; set; }
    public DateTime? ScheduledSendTime { get; set; }
    public int TotalCount { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
}

public class CampaignDetailsViewModel
{
    public int CampaignId { get; set; }
    public string CampaignName { get; set; } = string.Empty;
    public bool IsSent { get; set; }
    public bool PauseCampaign { get; set; }
    public int PendingCount { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public int DeliveredCount { get; set; }
    public int ReadCount { get; set; }
    public PagedList<CampaignDetail> Details { get; set; } = null!;
}
