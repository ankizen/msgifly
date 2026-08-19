namespace Msgifly.Web.Models.Entities;

/// <summary>One Contact's membership in an EmailList — Contact IS the email subscriber (no
/// separate subscriber table), so this just joins Contact to EmailList directly. Property named
/// Subscriber/SubscriberId for historical/readability reasons (this row's role, not a distinct
/// entity), but the type is Contact.</summary>
public class EmailSubscriberList
{
    public int Id { get; set; }
    public int SubscriberId { get; set; }
    public Contact Subscriber { get; set; } = null!;
    public int ListId { get; set; }
    public EmailList List { get; set; } = null!;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
