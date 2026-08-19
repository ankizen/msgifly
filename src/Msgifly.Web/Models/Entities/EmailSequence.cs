using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>A strictly linear, non-branching drip container — no conditions engine, matches
/// FluentCRM's actual Sequences product shape (distinct from the branching EmailAutomation engine).</summary>
public class EmailSequence
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public EmailSequenceStatus Status { get; set; } = EmailSequenceStatus.Draft;

    /// <summary>If set, adding a subscriber to this EmailList auto-enrolls them into the sequence.</summary>
    public int? AutoEnrollListId { get; set; }
    public EmailList? AutoEnrollList { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<EmailSequenceMail> Mails { get; set; } = new List<EmailSequenceMail>();
}
