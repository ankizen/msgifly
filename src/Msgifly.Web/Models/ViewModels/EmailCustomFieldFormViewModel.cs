using System.ComponentModel.DataAnnotations;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.ViewModels;

public class EmailCustomFieldFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string Label { get; set; } = string.Empty;

    public EmailCustomFieldType FieldType { get; set; } = EmailCustomFieldType.Text;

    /// <summary>Dropdown only — comma-separated options from the form, JSON-serialized on save.</summary>
    public string? OptionsCsv { get; set; }
}
