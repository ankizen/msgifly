using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>A bulk email blast to a filtered subscriber segment — Email Marketing's counterpart to Campaign.</summary>
public class EmailCampaign
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public EmailCampaignStatus Status { get; set; } = EmailCampaignStatus.Draft;

    public string FromName { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;

    public DateTime? ScheduledAt { get; set; }
    public bool SendNow { get; set; }

    /// <summary>true = every bulk-sendable subscriber; false = the Include/Exclude filters below.</summary>
    public bool SelectAll { get; set; }

    /// <summary>JSON int[] of EmailList ids, mirrors Campaign.FilterJson's JSON-blob idiom.</summary>
    public string? IncludeListIdsJson { get; set; }
    public string? ExcludeListIdsJson { get; set; }
    public string? IncludeTagIdsJson { get; set; }
    public string? ExcludeTagIdsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<EmailCampaignRecipient> Recipients { get; set; } = new List<EmailCampaignRecipient>();
}
