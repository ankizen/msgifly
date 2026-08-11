using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Delivery log for outbound generic model-CRUD webhooks (contacts/statuses/sources changes -> external URL).</summary>
public class WebhookLog
{
    public int Id { get; set; }
    public string Event { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public WebhookLogStatus Status { get; set; }
    public int Attempt { get; set; }
    public string? PayloadJson { get; set; }
    public string? ResponseJson { get; set; }
    public string? ErrorMessage { get; set; }
    public int? StatusCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsSuccessful => Status == WebhookLogStatus.Success;
}
