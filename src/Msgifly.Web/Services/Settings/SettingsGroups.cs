namespace Msgifly.Web.Services.Settings;

/// <summary>
/// Site identity/branding — the white-label touchpoint. Rendered into the layout on every page.
/// </summary>
public class GeneralSettings
{
    public string SiteName { get; set; } = "Msgifly";
    public string? SiteDescription { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string TimeFormat { get; set; } = "HH:mm";
    public string? SiteLogo { get; set; }
    public string? SiteDarkLogo { get; set; }
    public string? Favicon { get; set; }
    public string? CoverPageImage { get; set; }
    public string ActiveLanguage { get; set; } = "en";
}

public class SeoSettings
{
    public string MetaTitle { get; set; } = "Msgifly";
    public string? MetaDescription { get; set; }
}

/// <summary>
/// The Meta Developer App identity — one App shared by every Workspace (a Tech Provider only
/// ever registers one App and connects each of their own businesses' WhatsApp Business Accounts
/// under it via Embedded Signup, rather than creating a separate App per business). The WABA
/// connection itself (BusinessAccountId, AccessToken, phone numbers) is per-Workspace and lives
/// directly on the Workspace entity — see its doc comment. Access token/app secret are stored as
/// plain settings values here, same as the original; consider encrypting at rest in a later pass.
/// </summary>
public class MetaAppSettings
{
    public bool IsWebhookConnected { get; set; }

    public string? FacebookAppId { get; set; }
    public string? FacebookAppSecret { get; set; }
    public string? WebhookVerifyToken { get; set; }
    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>WhatsApp Embedded Signup configuration id, created in the Meta App dashboard under
    /// WhatsApp -> Embedded Signup -> Configurations. Needed to launch FB.login() for Phase 2.</summary>
    public string? EmbeddedSignupConfigId { get; set; }
}
