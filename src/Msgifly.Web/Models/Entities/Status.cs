namespace Msgifly.Web.Models.Entities;

/// <summary>Contact pipeline stage (e.g. New, Contacted, Qualified) with a UI badge color.</summary>
public class Status
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#4CAF50";
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
}
