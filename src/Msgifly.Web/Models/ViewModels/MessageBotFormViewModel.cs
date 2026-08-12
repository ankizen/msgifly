using System.ComponentModel.DataAnnotations;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.ViewModels;

public class MessageBotFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Recipient type")]
    public ContactType RelType { get; set; } = ContactType.Lead;

    [Required]
    [Display(Name = "Trigger mode")]
    public ReplyType ReplyType { get; set; } = ReplyType.Contains;

    [Display(Name = "Trigger keywords (comma-separated)")]
    public string TriggersInput { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Reply text")]
    public string ReplyText { get; set; } = string.Empty;

    public string? HeaderText { get; set; }
    public string? FooterText { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
