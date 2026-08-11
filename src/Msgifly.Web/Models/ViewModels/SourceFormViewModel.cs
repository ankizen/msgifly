using System.ComponentModel.DataAnnotations;

namespace Msgifly.Web.Models.ViewModels;

public class SourceFormViewModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(255)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;
}
