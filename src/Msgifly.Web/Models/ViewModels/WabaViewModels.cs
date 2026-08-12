using System.ComponentModel.DataAnnotations;
using Msgifly.Web.Services.WhatsApp;

namespace Msgifly.Web.Models.ViewModels;

public class WabaWebhookFormViewModel
{
    [Required]
    [Display(Name = "Facebook App ID")]
    public string FacebookAppId { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Facebook App Secret")]
    public string FacebookAppSecret { get; set; } = string.Empty;
}

public class WabaAccountFormViewModel
{
    [Required]
    [Display(Name = "WhatsApp Business Account ID")]
    public string BusinessAccountId { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Permanent Access Token")]
    public string AccessToken { get; set; } = string.Empty;
}

public class BusinessProfileFormViewModel
{
    [Display(Name = "About")]
    [MaxLength(139)]
    public string? About { get; set; }

    [Display(Name = "Email")]
    [EmailAddress]
    public string? Email { get; set; }

    [Display(Name = "Website")]
    [Url]
    public string? Website { get; set; }

    [Display(Name = "Industry")]
    public string? Vertical { get; set; }
}

public class WabaIndexViewModel
{
    public bool IsWebhookConnected { get; set; }
    public bool IsAccountConnected { get; set; }

    public string WebhookUrl { get; set; } = string.Empty;
    public string? WebhookVerifyToken { get; set; }

    public List<PhoneNumberInfo> PhoneNumbers { get; set; } = [];
    public string? DefaultPhoneNumberId { get; set; }
    public string? DefaultPhoneNumber { get; set; }

    public BusinessProfileInfo? BusinessProfile { get; set; }

    public WabaWebhookFormViewModel WebhookForm { get; set; } = new();
    public WabaAccountFormViewModel AccountForm { get; set; } = new();
    public BusinessProfileFormViewModel ProfileForm { get; set; } = new();
}
