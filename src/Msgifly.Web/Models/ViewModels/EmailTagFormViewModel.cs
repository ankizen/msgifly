using System.ComponentModel.DataAnnotations;

namespace Msgifly.Web.Models.ViewModels;

public class EmailTagFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;
}
