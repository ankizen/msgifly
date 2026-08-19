namespace Msgifly.Web.Models.Entities;

/// <summary>One subscriber's assignment of an EmailTag.</summary>
public class EmailSubscriberTag
{
    public int Id { get; set; }
    public int SubscriberId { get; set; }
    public EmailSubscriber Subscriber { get; set; } = null!;
    public int TagId { get; set; }
    public EmailTag Tag { get; set; } = null!;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
