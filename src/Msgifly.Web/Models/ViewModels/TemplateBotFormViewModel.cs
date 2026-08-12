using System.ComponentModel.DataAnnotations;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.ViewModels;

public class TemplateBotFormViewModel
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

    [Required]
    [Display(Name = "Trigger mode")]
    public ReplyType ReplyType { get; set; } = ReplyType.Contains;

    [Display(Name = "Trigger keywords (comma-separated)")]
    public string TriggersInput { get; set; } = string.Empty;

    public string? HeaderMediaUrl { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public CampaignParamInput[] HeaderParams { get; set; } = new CampaignParamInput[1];
    public CampaignParamInput[] BodyParams { get; set; } = new CampaignParamInput[CampaignFormViewModel.MaxBodyParams];

    public List<TemplateOption> TemplateOptions { get; set; } = [];
}
