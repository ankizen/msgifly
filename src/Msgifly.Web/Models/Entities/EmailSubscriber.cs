using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>
/// Email Marketing's own contact record — deliberately separate from Contact, not a reuse: Contact
/// is WhatsApp-phone-centric (no bulk-sendable status, no list/tag pivots, no unsubscribe concept).
/// ContactId is an optional cross-link for a future unified view; a pure email-only lead needs no
/// Contact row at all.
/// </summary>
public class EmailSubscriber
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }

    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>Plain string, not FK — optional cross-reference only.</summary>
    public string? Phone { get; set; }

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public ContactType Type { get; set; } = ContactType.Lead;
    public EmailSubscriberStatus Status { get; set; } = EmailSubscriberStatus.Subscribed;

    public int? SourceId { get; set; }
    public Source? Source { get; set; }

    /// <summary>Values keyed by EmailCustomField.Key — a JSON blob, not EAV.</summary>
    public string CustomFieldsJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string FullName => $"{FirstName} {LastName}".Trim();
}
