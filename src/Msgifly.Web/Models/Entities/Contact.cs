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

    /// <summary>Primary matching key for inbound webhook contact resolution.</summary>
    public string Phone { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
    public int? AddedFromId { get; set; }
    public DateTime? DateAssigned { get; set; }
    public DateTime? LastStatusChange { get; set; }
    public string? DefaultLanguage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ContactNote> Notes { get; set; } = new List<ContactNote>();

    public string FullName => $"{FirstName} {LastName}".Trim();
}
