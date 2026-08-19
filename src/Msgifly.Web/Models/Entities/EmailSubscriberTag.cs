namespace Msgifly.Web.Models.Entities;

/// <summary>One Contact's assignment of an EmailTag — Contact IS the email subscriber (no
/// separate subscriber table). Property named Subscriber/SubscriberId for historical/readability
/// reasons, but the type is Contact.</summary>
public class EmailSubscriberTag
{
    public int Id { get; set; }
    public int SubscriberId { get; set; }
    public Contact Subscriber { get; set; } = null!;
    public int TagId { get; set; }
    public EmailTag Tag { get; set; } = null!;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
