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
/// WhatsApp Business Account connection — one WABA/App per installation (single-tenant, same as
/// the original — see master doc §8.5). Access token/app secret are stored as plain settings
/// values here, same as the original; consider encrypting at rest in a later pass.
/// </summary>
public class WhatsAppSettings
{
    public bool IsWebhookConnected { get; set; }
    public bool IsAccountConnected { get; set; }

    public string? FacebookAppId { get; set; }
    public string? FacebookAppSecret { get; set; }
    public string? WebhookVerifyToken { get; set; }

    public string? BusinessAccountId { get; set; }
    public string? AccessToken { get; set; }
    public string ApiVersion { get; set; } = "v21.0";

    public string? DefaultPhoneNumber { get; set; }
    public string? DefaultPhoneNumberId { get; set; }
    public string? ProfilePictureUrl { get; set; }

    public DateTime? LastHealthCheckAt { get; set; }
    public string? HealthStatusJson { get; set; }
}
