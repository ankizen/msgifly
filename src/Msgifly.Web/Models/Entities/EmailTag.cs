namespace Msgifly.Web.Models.Entities;

/// <summary>A subscriber label, separate from EmailList — two dedicated pivots (this app's
/// convention) rather than a stringly-typed shared object_type pivot.</summary>
public class EmailTag
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<EmailSubscriberTag> Members { get; set; } = new List<EmailSubscriberTag>();
}
