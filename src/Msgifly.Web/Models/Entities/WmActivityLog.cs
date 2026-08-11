using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Audit log of every outbound WhatsApp Cloud API send (campaign, bot, or manual).</summary>
public class WmActivityLog
{
    public int Id { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? BusinessAccountId { get; set; }
    public string ResponseCode { get; set; } = string.Empty;

    public ActivityLogCategory Category { get; set; }

    /// <summary>Meaning depends on Category: campaign id, message_bot id, or template_bot id.</summary>
    public int? CategoryId { get; set; }

    public ContactType? RelType { get; set; }
    public int? RelatedContactId { get; set; }

    public string? RawRequestJson { get; set; }
    public string? RawResponseJson { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
