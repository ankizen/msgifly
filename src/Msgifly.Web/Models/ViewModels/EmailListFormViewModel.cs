using System.ComponentModel.DataAnnotations;

namespace Msgifly.Web.Models.ViewModels;

public class EmailListFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
