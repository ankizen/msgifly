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

/// <summary>
/// Lets the running app register a newly-configured workspace tracking domain with our own Coolify
/// deployment (see CoolifyDomainService) — routing/TLS for a docker-compose app is driven by domains
/// baked into the container's labels at deploy time, so adding a domain means updating Coolify's
/// stored domain list and triggering a redeploy. Doing this from inside the app itself (rather than
/// requiring a human to run it manually every time a business sets up their own domain) is what
/// makes the feature actually self-service.
/// </summary>
public class CoolifyIntegrationSettings
{
    public string? BaseUrl { get; set; } = "https://coolify.swarnapp.com";
    public string? ApiToken { get; set; }
    public string? ApplicationUuid { get; set; }

    /// <summary>The docker-compose service key this app's domains live under (Coolify's
    /// docker_compose_domains is keyed per service, e.g. {"web": {"domain": "https://..."}}).</summary>
    public string ComposeServiceName { get; set; } = "web";

    /// <summary>Domains that must never be dropped from the list, whatever else changes — a hard
    /// safety net so a bug here can never accidentally take app.msgifly.com itself offline.</summary>
    public List<string> RequiredDomains { get; set; } = [];
}
