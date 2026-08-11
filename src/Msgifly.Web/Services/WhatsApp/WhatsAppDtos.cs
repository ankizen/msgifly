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
