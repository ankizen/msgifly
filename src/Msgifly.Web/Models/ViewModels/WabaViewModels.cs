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

    [Display(Name = "Embedded Signup configuration ID")]
    public string? EmbeddedSignupConfigId { get; set; }
}

public class EmbeddedSignupCompleteRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    public string WabaId { get; set; } = string.Empty;

    public string? PhoneNumberId { get; set; }
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

    [Display(Name = "Description")]
    [MaxLength(512)]
    public string? Description { get; set; }

    [Display(Name = "Email")]
    [EmailAddress]
    public string? Email { get; set; }

    [Display(Name = "Address")]
    [MaxLength(256)]
    public string? Address { get; set; }

    [Display(Name = "Website")]
    [Url]
    public string? Website { get; set; }

    [Display(Name = "Website 2")]
    [Url]
    public string? Website2 { get; set; }

    [Display(Name = "Industry")]
    public string? Vertical { get; set; }
}

public class WabaIndexViewModel
{
    public bool IsWebhookConnected { get; set; }
    public bool IsAccountConnected { get; set; }

    public string WebhookUrl { get; set; } = string.Empty;
    public string? WebhookVerifyToken { get; set; }
    public string? FacebookAppId { get; set; }
    public string? EmbeddedSignupConfigId { get; set; }
    public string ApiVersion { get; set; } = "v21.0";

    public List<PhoneNumberInfo> PhoneNumbers { get; set; } = [];
    public string? DefaultPhoneNumberId { get; set; }
    public string? DefaultPhoneNumber { get; set; }

    public BusinessProfileInfo? BusinessProfile { get; set; }

    public WabaWebhookFormViewModel WebhookForm { get; set; } = new();
    public WabaAccountFormViewModel AccountForm { get; set; } = new();
    public BusinessProfileFormViewModel ProfileForm { get; set; } = new();
}
