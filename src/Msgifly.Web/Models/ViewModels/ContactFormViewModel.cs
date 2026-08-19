using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.ViewModels;

public class ContactFormViewModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(255)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [StringLength(255)]
    public string? Company { get; set; }

    [Required]
    public ContactType Type { get; set; } = ContactType.Lead;

    public string? Description { get; set; }

    [Display(Name = "Country code")]
    public string? CountryCode { get; set; }

    public string? Zip { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Address { get; set; }

    [Display(Name = "Assigned to")]
    public int? AssignedToId { get; set; }

    [Required]
    [Display(Name = "Status")]
    public int StatusId { get; set; }

    [Required]
    [Display(Name = "Source")]
    public int SourceId { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    public string? Website { get; set; }

    [Required]
    [Phone]
    public string Phone { get; set; } = string.Empty;

    [Display(Name = "Enabled")]
    public bool IsEnabled { get; set; } = true;

    // --- Email Marketing (Contact IS the email subscriber — no separate list) ---
    [Display(Name = "Email status")]
    public EmailSubscriberStatus EmailStatus { get; set; } = EmailSubscriberStatus.Subscribed;

    public List<int> SelectedListIds { get; set; } = [];
    public List<int> SelectedTagIds { get; set; } = [];

    /// <summary>Keyed by EmailCustomField.Key — rendered dynamically from ViewData["EmailCustomFields"].</summary>
    public Dictionary<string, string> EmailCustomFieldValues { get; set; } = [];

    public List<SelectListItem> StatusOptions { get; set; } = [];
    public List<SelectListItem> SourceOptions { get; set; } = [];
    public List<SelectListItem> AssigneeOptions { get; set; } = [];

    public List<ContactNote> Notes { get; set; } = [];
}
