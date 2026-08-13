using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>
/// A reusable, named audience for campaigns — either a fixed member list (Static, built via
/// checkbox-select or CSV upload) or a saved filter (Dynamic, re-evaluated against current
/// contacts every time it's used). Resolved into a flat contact-id list at the moment a campaign
/// is created, so campaign send logic never needs to know which kind of group it came from.
/// </summary>
public class ContactGroup
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ContactGroupType Type { get; set; } = ContactGroupType.Static;

    /// <summary>Only meaningful when Type == Dynamic — JSON: {"relType":"Lead","statusIds":[1,2],"sourceIds":[3]}. Null/empty entries mean "any".</summary>
    public string? FilterJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ContactGroupMember> Members { get; set; } = new List<ContactGroupMember>();
}
