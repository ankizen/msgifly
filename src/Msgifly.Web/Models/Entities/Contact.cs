using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Core CRM entity — a lead or customer reachable over WhatsApp.</summary>
public class Contact
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Company { get; set; }
    public ContactType Type { get; set; } = ContactType.Lead;
    public string? Description { get; set; }
    public string? CountryCode { get; set; }
    public string? Zip { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Address { get; set; }

    public int? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    public int StatusId { get; set; }
    public Status Status { get; set; } = null!;

    public int SourceId { get; set; }
    public Source Source { get; set; } = null!;

    public string? Email { get; set; }
    public string? Website { get; set; }

    /// <summary>Set only for contacts imported via Lead Ads sync — which specific Instant Form
    /// they came from (a Source of "Facebook Lead Ads" alone doesn't distinguish between a Page's
    /// several forms, but the CRM needs to for filtering and per-form automation review).</summary>
    public string? LeadAdsFormId { get; set; }
    public string? LeadAdsFormName { get; set; }

    /// <summary>Primary matching key for inbound webhook contact resolution.</summary>
    public string Phone { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
    public int? AddedFromId { get; set; }
    public DateTime? DateAssigned { get; set; }
    public DateTime? LastStatusChange { get; set; }
    public string? DefaultLanguage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Email opt-in state — only Subscribed/Transactional are bulk-sendable from Email
    /// Marketing. A Contact IS the email subscriber (no separate list): the same person who came
    /// in as a WhatsApp/Facebook lead is who gets emailed, so this lives directly here rather than
    /// duplicating the record. Unrelated to StatusId (that's the CRM pipeline stage).</summary>
    public EmailSubscriberStatus EmailStatus { get; set; } = EmailSubscriberStatus.Subscribed;

    /// <summary>Values keyed by EmailCustomField.Key — a JSON blob, not EAV.</summary>
    public string EmailCustomFieldsJson { get; set; } = "{}";

    public ICollection<ContactNote> Notes { get; set; } = new List<ContactNote>();

    public string FullName => $"{FirstName} {LastName}".Trim();
}
