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
