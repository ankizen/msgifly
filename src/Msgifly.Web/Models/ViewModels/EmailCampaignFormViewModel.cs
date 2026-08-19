using System.ComponentModel.DataAnnotations;

namespace Msgifly.Web.Models.ViewModels;

public class EmailCampaignFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string FromName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string FromEmail { get; set; } = string.Empty;

    [Required]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string BodyHtml { get; set; } = string.Empty;

    public bool SendNow { get; set; } = true;
    public DateTime? ScheduledAt { get; set; }

    public bool SelectAll { get; set; }
    public List<int> IncludeListIds { get; set; } = [];
    public List<int> ExcludeListIds { get; set; } = [];
    public List<int> IncludeTagIds { get; set; } = [];
    public List<int> ExcludeTagIds { get; set; } = [];
}
