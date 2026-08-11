namespace Msgifly.Web.Models.Entities;

public class ContactNote
{
    public int Id { get; set; }
    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
