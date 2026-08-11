using System.ComponentModel.DataAnnotations;

namespace Msgifly.Web.Models.ViewModels;

public class StatusFormViewModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Enter a hex color like #4CAF50.")]
    [Display(Name = "Color")]
    public string Color { get; set; } = "#4CAF50";

    [Display(Name = "Default status for new contacts")]
    public bool IsDefault { get; set; }
}
