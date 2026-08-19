using System.ComponentModel.DataAnnotations;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.ViewModels;

public class EmailSubscriberFormViewModel
{
    public int? Id { get; set; }

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }

    public ContactType Type { get; set; } = ContactType.Lead;
    public EmailSubscriberStatus Status { get; set; } = EmailSubscriberStatus.Subscribed;
    public int? SourceId { get; set; }

    public List<int> SelectedListIds { get; set; } = [];
    public List<int> SelectedTagIds { get; set; } = [];

    /// <summary>Keyed by EmailCustomField.Key — rendered dynamically from ViewData["CustomFields"].</summary>
    public Dictionary<string, string> CustomFieldValues { get; set; } = [];
}

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
