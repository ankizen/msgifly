namespace Msgifly.Web.Services.LeadAds;

public record LeadAdsResult(bool Success, string? ErrorMessage = null)
{
    public static LeadAdsResult Ok() => new(true);
    public static LeadAdsResult Fail(string message) => new(false, message);
}

public record LeadAdsResult<T>(bool Success, T? Data, string? ErrorMessage = null)
{
    public static LeadAdsResult<T> Ok(T data) => new(true, data);
    public static LeadAdsResult<T> Fail(string message) => new(false, default, message);
}

public class FacebookPageInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Page-scoped access token, returned alongside the page itself by /me/accounts — this, not the user token, is what every subsequent Graph call against this page uses.</summary>
    public string AccessToken { get; set; } = string.Empty;
}

public class LeadFormInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class LeadInfo
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedTime { get; set; }

    /// <summary>Raw field_data from Meta, keyed by field name (e.g. "full_name", "email", "phone_number") — Lead Ads forms vary in exactly which fields they ask for, so callers pick what they recognize rather than assuming a fixed shape.</summary>
    public Dictionary<string, List<string>> Fields { get; set; } = new();
}
