using System.ComponentModel.DataAnnotations;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.ViewModels;

public class EmailSequenceFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public EmailSequenceStatus Status { get; set; } = EmailSequenceStatus.Draft;

    /// <summary>If set, adding a subscriber to this list auto-enrolls them into the sequence.</summary>
    public int? AutoEnrollListId { get; set; }

    public List<EmailSequenceMailInput> Mails { get; set; } = [];
}

public class EmailSequenceMailInput
{
    public int? Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public int DelayAmount { get; set; } = 1;
    public string DelayUnit { get; set; } = "days";
}
