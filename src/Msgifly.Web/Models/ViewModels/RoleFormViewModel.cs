using System.ComponentModel.DataAnnotations;

namespace Msgifly.Web.Models.ViewModels;

public class RoleFormViewModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    public List<string> SelectedPermissions { get; set; } = [];

    public string[] AllPermissions { get; set; } = [];
}
