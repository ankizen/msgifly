namespace Msgifly.Web.Models.Entities;

/// <summary>One subscriber's membership in an EmailList.</summary>
public class EmailSubscriberList
{
    public int Id { get; set; }
    public int SubscriberId { get; set; }
    public EmailSubscriber Subscriber { get; set; } = null!;
    public int ListId { get; set; }
    public EmailList List { get; set; } = null!;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
