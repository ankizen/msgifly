namespace Msgifly.Web.Models.Entities;

/// <summary>
/// One tracked URL-button send. Created right before the outbound Graph API call (so the token
/// resolves the instant the message could theoretically be tapped), then backfilled with
/// WhatsappMessageId once Meta accepts the send. The redirect controller looks a click up purely by
/// Token — WhatsappMessageId is only there to correlate a click back to the matching
/// ChatMessage/CampaignDetail row, mirroring WhatsAppWebhookController's existing reply-attribution
/// lookup rather than adding a new relational concept.
/// </summary>
public class TemplateButtonClick
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }

    /// <summary>Unique — the /r/{token} lookup key, embedded as the button's dynamic {{1}} suffix.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The real destination as the admin typed it — captured at send time so a later edit
    /// to the template can't retroactively change where an already-sent link redirects to.</summary>
    public string DestinationUrl { get; set; } = string.Empty;

    public string TemplateName { get; set; } = string.Empty;
    public string? ButtonText { get; set; }
    public int ButtonIndex { get; set; }

    /// <summary>Null until the send succeeds; stays null forever if the send itself failed
    /// (harmless orphan row — negligible volume at this scale).</summary>
    public string? WhatsappMessageId { get; set; }

    public int ClickCount { get; set; }
    public DateTime? FirstClickedAt { get; set; }
    public DateTime? LastClickedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
