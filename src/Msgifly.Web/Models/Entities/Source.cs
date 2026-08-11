namespace Msgifly.Web.Models.Entities;

/// <summary>Lead-source taxonomy (e.g. Facebook, WhatsApp, Website).</summary>
public class Source
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
}
