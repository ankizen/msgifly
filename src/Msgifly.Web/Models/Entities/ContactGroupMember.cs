namespace Msgifly.Web.Models.Entities;

/// <summary>One contact's membership in a Static ContactGroup.</summary>
public class ContactGroupMember
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public ContactGroup Group { get; set; } = null!;
    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
