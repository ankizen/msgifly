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
    public string? Websites { get; set; }
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
