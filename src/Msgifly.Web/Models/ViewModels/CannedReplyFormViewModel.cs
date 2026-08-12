using System.ComponentModel.DataAnnotations;

namespace Msgifly.Web.Models.ViewModels;

public class CannedReplyFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Reply text")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Visible to all agents")]
    public bool IsPublic { get; set; } = true;
}
