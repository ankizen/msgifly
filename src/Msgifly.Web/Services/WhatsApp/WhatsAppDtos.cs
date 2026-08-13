namespace Msgifly.Web.Services.WhatsApp;

public record WhatsAppResult(bool Success, string? ErrorMessage = null)
{
    public static WhatsAppResult Ok() => new(true);
    public static WhatsAppResult Fail(string message) => new(false, message);
}

public record WhatsAppResult<T>(bool Success, T? Data, string? ErrorMessage = null)
{
    public static WhatsAppResult<T> Ok(T data) => new(true, data);
    public static WhatsAppResult<T> Fail(string message) => new(false, default, message);
}

/// <summary>
/// The credentials WhatsAppService actually needs for one Graph API call, merged from two
/// sources: the current Workspace's WABA connection fields (BusinessAccountId, AccessToken,
/// DefaultPhoneNumberId — per-business) and the global MetaAppSettings (FacebookAppId/Secret,
/// ApiVersion — one Meta App shared by every Workspace). See
/// WhatsAppService.GetSettingsAsync for how this gets built.
/// </summary>
public class ResolvedWhatsAppSettings
{
    public string? FacebookAppId { get; set; }
    public string? FacebookAppSecret { get; set; }
    public string ApiVersion { get; set; } = "v21.0";

    public string? BusinessAccountId { get; set; }
    public string? AccessToken { get; set; }
    public string? DefaultPhoneNumberId { get; set; }
    public string? DefaultPhoneNumber { get; set; }
}

public class PhoneNumberInfo
{
    public string Id { get; set; } = string.Empty;
    public string DisplayPhoneNumber { get; set; } = string.Empty;
    public string? VerifiedName { get; set; }
    public string? QualityRating { get; set; }
}

public class BusinessProfileInfo
{
    public string? About { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    public string? Vertical { get; set; }
    public string? Website { get; set; }
    public string? Website2 { get; set; }
}

/// <summary>Everything needed to build a Graph API template-send payload for one recipient.</summary>
public class TemplateSendRequest
{
    public string TemplateName { get; set; } = string.Empty;
    public string Language { get; set; } = "en_US";

    /// <summary>TEXT | IMAGE | DOCUMENT | VIDEO | null (no header).</summary>
    public string? HeaderFormat { get; set; }

    /// <summary>Substituted value for a TEXT header's single {{1}} placeholder, if any.</summary>
    public string? HeaderText { get; set; }

    /// <summary>Publicly reachable URL for IMAGE/DOCUMENT/VIDEO headers.</summary>
    public string? HeaderMediaUrl { get; set; }

    public List<string> BodyParams { get; set; } = [];
}

/// <summary>Result of GET /{media-id} — a short-lived signed CDN URL, not a permanent link.</summary>
public class MediaInfo
{
    public string Url { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string? Sha256 { get; set; }
    public long FileSizeBytes { get; set; }
    public string MediaId { get; set; } = string.Empty;
}

/// <summary>image | video | audio | document | sticker. Provide either Link (public HTTPS URL) or MediaId (previously uploaded), not both.</summary>
public class MediaMessageRequest
{
    public string MediaType { get; set; } = "image";
    public string? Link { get; set; }
    public string? MediaId { get; set; }
    public string? Caption { get; set; }
    public string? Filename { get; set; }
}

public class LocationMessageRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
}

public class ContactCardRequest
{
    public string FormattedName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Organization { get; set; }
}

/// <summary>Up to 3 quick-reply buttons. Id is echoed back in the recipient's button_reply on tap.</summary>
public record InteractiveButton(string Id, string Title);

public record InteractiveListRow(string Id, string Title, string? Description = null);

public record InteractiveListSection(string Title, List<InteractiveListRow> Rows);

/// <summary>What the local Business Profile settings screen can change (about/email/address/description/websites/vertical/photo — the profile photo is a separate media-upload step).</summary>
public class BusinessProfileUpdateRequest
{
    public string? About { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    public string? Website { get; set; }
    public string? Website2 { get; set; }

    /// <summary>Meta's fixed industry vertical enum, e.g. "RETAIL", "PROF_SERVICES", "OTHER".</summary>
    public string? Vertical { get; set; }
}

/// <summary>"Ice breakers" (prompts, max 4) shown only in a brand-new/empty chat, and a "/" commands
/// menu (max 30) — Meta calls the combined feature "conversational automation." Tapping either one
/// just sends a normal inbound text message; Meta gives no signal distinguishing that from the
/// customer typing the same words themselves.</summary>
public class ConversationalAutomationInfo
{
    public List<string> Prompts { get; set; } = [];
    public List<CommandInfo> Commands { get; set; } = [];
}

public class CommandInfo
{
    public string CommandName { get; set; } = string.Empty;
    public string CommandDescription { get; set; } = string.Empty;
}

/// <summary>QUICK_REPLY | URL | PHONE_NUMBER | COPY_CODE — see TemplateValidator for the per-type rules Meta enforces.</summary>
public class TemplateButtonRequest
{
    public string Type { get; set; } = "QUICK_REPLY";
    public string Text { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? PhoneNumber { get; set; }

    /// <summary>Example value — required when a URL button's url contains {{1}}, or always for COPY_CODE.</summary>
    public string? Example { get; set; }
}

/// <summary>Sample values Meta requires 1:1 with the {{N}} variables in the header/body text, used for human review.</summary>
public class TemplateSampleValues
{
    public List<string> Header { get; set; } = [];
    public List<string> Body { get; set; } = [];
}

/// <summary>One row from GET /{WABA_ID}?fields=pricing_analytics...— per-message cost/volume, optionally broken down by PricingCategory/PricingType/Country when those dimensions are requested. The live replacement for the deprecated conversation_analytics field (Meta moved to per-message pricing 2025-07-01).</summary>
public class PricingAnalyticsDataPoint
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int Volume { get; set; }
    public decimal Cost { get; set; }
    public string? PricingCategory { get; set; }
    public string? PricingType { get; set; }
}

/// <summary>One Flow as listed by GET /{WABA_ID}/flows — used by SyncFlowsAsync to upsert local rows.</summary>
public class FlowSummary
{
    public string MetaFlowId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = [];
    public string Status { get; set; } = string.Empty;
}

/// <summary>Everything needed to create or edit a template on Meta — the local-authoring equivalent of TemplateSendRequest.</summary>
public class TemplateCreateRequest
{
    /// <summary>Meta rule: lowercase a-z, digits, underscore only.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>MARKETING | UTILITY | AUTHENTICATION (Authentication isn't supported here — same restriction as the reference implementation this was ported from; manage those in Meta Business Manager directly).</summary>
    public string Category { get; set; } = "MARKETING";

    public string Language { get; set; } = "en_US";

    /// <summary>text | image | video | document | null (no header).</summary>
    public string? HeaderType { get; set; }

    /// <summary>TEXT header content — at most one {{1}} placeholder.</summary>
    public string? HeaderContent { get; set; }

    /// <summary>Public sample URL for image/video/document headers.</summary>
    public string? HeaderMediaUrl { get; set; }

    public string BodyText { get; set; } = string.Empty;
    public string? FooterText { get; set; }
    public List<TemplateButtonRequest> Buttons { get; set; } = [];
    public TemplateSampleValues SampleValues { get; set; } = new();
}
