namespace Msgifly.Web.Models.Entities;

/// <summary>A named subscriber list — targetable by EmailCampaign and the auto-enroll trigger of EmailSequence.</summary>
public class EmailList
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<EmailSubscriberList> Members { get; set; } = new List<EmailSubscriberList>();
}
