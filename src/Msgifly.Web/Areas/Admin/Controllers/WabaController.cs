using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.Settings;
using Msgifly.Web.Services.WhatsApp;
using Msgifly.Web.Services.Workspaces;
using QRCoder;

namespace Msgifly.Web.Areas.Admin.Controllers;

/// <summary>
/// "Connect WABA" for the CURRENT workspace. The Meta App identity (FacebookAppId/Secret,
/// webhook verify token) is global — one App shared by every workspace — so ConnectWebhook
/// writes to MetaAppSettings; every other action here reads/writes the current Workspace's own
/// WABA connection fields directly.
/// </summary>
[Area("Admin")]
[Authorize]
public class WabaController : Controller
{
    private readonly ISettingsService _settingsService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly Services.WhatsApp.EmbeddedSignupService _embeddedSignupService;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public WabaController(
        ISettingsService settingsService,
        IWhatsAppService whatsAppService,
        Services.WhatsApp.EmbeddedSignupService embeddedSignupService,
        ApplicationDbContext db,
        ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _settingsService = settingsService;
        _whatsAppService = whatsAppService;
        _embeddedSignupService = embeddedSignupService;
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    private Task<Models.Entities.Workspace> CurrentWorkspaceAsync() =>
        _db.Workspaces.FirstAsync(w => w.Id == _workspaceAccessor.WorkspaceId);

    [Authorize(Policy = "connect_account.view")]
    public async Task<IActionResult> Index()
    {
        var metaApp = await _settingsService.GetAsync<MetaAppSettings>(nameof(MetaAppSettings));
        var workspace = await CurrentWorkspaceAsync();

        var model = new WabaIndexViewModel
        {
            IsWebhookConnected = metaApp.IsWebhookConnected,
            IsAccountConnected = workspace.IsAccountConnected,
            WebhookUrl = $"{Request.Scheme}://{Request.Host}/whatsapp/webhook",
            WebhookVerifyToken = metaApp.WebhookVerifyToken,
            FacebookAppId = metaApp.FacebookAppId,
            EmbeddedSignupConfigId = metaApp.EmbeddedSignupConfigId,
            ApiVersion = metaApp.ApiVersion,
            DefaultPhoneNumberId = workspace.DefaultPhoneNumberId,
            DefaultPhoneNumber = workspace.DefaultPhoneNumber,
        };

        if (workspace.IsAccountConnected)
        {
            var phoneNumbers = await _whatsAppService.GetPhoneNumbersAsync();
            if (phoneNumbers.Success)
            {
                model.PhoneNumbers = phoneNumbers.Data!;
            }
            else
            {
                this.Notify($"Couldn't refresh phone numbers: {phoneNumbers.ErrorMessage}", "danger");
            }

            if (!string.IsNullOrWhiteSpace(workspace.DefaultPhoneNumberId))
            {
                var profile = await _whatsAppService.GetBusinessProfileAsync(workspace.DefaultPhoneNumberId);
                if (profile.Success)
                {
                    model.BusinessProfile = profile.Data;
                    model.ProfileForm = new BusinessProfileFormViewModel
                    {
                        About = profile.Data!.About,
                        Description = profile.Data.Description,
                        Email = profile.Data.Email,
                        Address = profile.Data.Address,
                        Website = profile.Data.Website,
                        Website2 = profile.Data.Website2,
                        Vertical = profile.Data.Vertical,
                    };
                }
            }
        }

        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBusinessProfile([Bind(Prefix = "ProfileForm")] BusinessProfileFormViewModel form)
    {
        var workspace = await CurrentWorkspaceAsync();
        if (string.IsNullOrWhiteSpace(workspace.DefaultPhoneNumberId))
        {
            this.Notify("Choose a default number first.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var result = await _whatsAppService.UpdateBusinessProfileAsync(workspace.DefaultPhoneNumberId, new BusinessProfileUpdateRequest
        {
            About = form.About,
            Description = form.Description,
            Email = form.Email,
            Address = form.Address,
            Website = form.Website,
            Website2 = form.Website2,
            Vertical = form.Vertical,
        });

        this.Notify(result.Success ? "Business profile updated." : $"Couldn't update profile: {result.ErrorMessage}", result.Success ? "success" : "danger");
        return RedirectToAction(nameof(Index));
    }

    private const long MaxProfilePictureBytes = 5 * 1024 * 1024; // Meta's own cap for whatsapp_business_profile photos

    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadProfilePicture(IFormFile file)
    {
        var workspace = await CurrentWorkspaceAsync();
        if (string.IsNullOrWhiteSpace(workspace.DefaultPhoneNumberId))
        {
            this.Notify("Choose a default number first.", "danger");
            return RedirectToAction(nameof(Index));
        }

        if (file is null || file.Length == 0)
        {
            this.Notify("Choose an image to upload.", "danger");
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > MaxProfilePictureBytes)
        {
            this.Notify("Image is larger than WhatsApp's 5 MB limit for profile photos.", "danger");
            return RedirectToAction(nameof(Index));
        }

        await using var stream = file.OpenReadStream();
        var uploadResult = await _whatsAppService.UploadProfilePictureHandleAsync(stream, file.FileName, file.Length, file.ContentType);
        if (!uploadResult.Success)
        {
            this.Notify($"Upload failed: {uploadResult.ErrorMessage}", "danger");
            return RedirectToAction(nameof(Index));
        }

        var result = await _whatsAppService.UpdateBusinessProfilePictureAsync(workspace.DefaultPhoneNumberId, uploadResult.Data!);
        this.Notify(result.Success ? "Profile photo updated." : $"Couldn't set profile photo: {result.ErrorMessage}", result.Success ? "success" : "danger");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Registers the Meta App identity — global, shared by every workspace, done once regardless of how many businesses get connected below.</summary>
    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConnectWebhook([Bind(Prefix = "WebhookForm")] WabaWebhookFormViewModel form)
    {
        if (!ModelState.IsValid)
        {
            this.Notify("Enter both the Facebook App ID and App Secret.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var settings = await _settingsService.GetAsync<MetaAppSettings>(nameof(MetaAppSettings));
        settings.FacebookAppId = form.FacebookAppId;
        settings.FacebookAppSecret = form.FacebookAppSecret;
        settings.EmbeddedSignupConfigId = string.IsNullOrWhiteSpace(form.EmbeddedSignupConfigId) ? settings.EmbeddedSignupConfigId : form.EmbeddedSignupConfigId.Trim();
        settings.WebhookVerifyToken ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        settings.IsWebhookConnected = true;
        await _settingsService.SaveAsync(nameof(MetaAppSettings), settings);

        this.Notify("Webhook app registered. Add the webhook URL and verify token to your Meta App's WhatsApp configuration, then connect a Business Account below.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConnectAccount([Bind(Prefix = "AccountForm")] WabaAccountFormViewModel form)
    {
        if (!ModelState.IsValid)
        {
            this.Notify("Enter both the Business Account ID and Access Token.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var workspace = await CurrentWorkspaceAsync();
        workspace.BusinessAccountId = form.BusinessAccountId;
        workspace.AccessToken = form.AccessToken;
        workspace.ConnectionMethod = "manual";
        workspace.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FinalizeConnectionAsync(workspace, preferredPhoneNumberId: null);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Step 2 done via WhatsApp Embedded Signup instead of pasting a token manually: the frontend
    /// FB.login() flow hands back an authorization code plus the WABA (and usually phone number)
    /// id chosen inside the Facebook popup, delivered here as a plain form post from the hidden
    /// form in Waba/Index.cshtml (see embedded-signup.js).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteEmbeddedSignup(EmbeddedSignupCompleteRequest request)
    {
        if (!ModelState.IsValid)
        {
            this.Notify("The Facebook signup flow didn't return the expected data — try again.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var tokenResult = await _embeddedSignupService.ExchangeCodeForLongLivedTokenAsync(request.Code);
        if (!tokenResult.Success)
        {
            this.Notify($"Couldn't complete signup: {tokenResult.ErrorMessage}", "danger");
            return RedirectToAction(nameof(Index));
        }

        var workspace = await CurrentWorkspaceAsync();
        workspace.BusinessAccountId = request.WabaId;
        workspace.AccessToken = tokenResult.Data;
        workspace.ConnectionMethod = "embedded_signup";
        workspace.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await FinalizeConnectionAsync(workspace, request.PhoneNumberId);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Shared by both connection paths once a Workspace has a BusinessAccountId + AccessToken: verify it works, sync templates, subscribe the webhook, and pick a default sending number.</summary>
    private async Task FinalizeConnectionAsync(Models.Entities.Workspace workspace, string? preferredPhoneNumberId)
    {
        var phoneNumbers = await _whatsAppService.GetPhoneNumbersAsync();
        if (!phoneNumbers.Success)
        {
            this.Notify($"Couldn't connect: {phoneNumbers.ErrorMessage}", "danger");
            return;
        }

        var syncResult = await _whatsAppService.SyncTemplatesAsync();
        var subscribeResult = await _whatsAppService.SubscribeWebhookAsync();

        workspace.IsAccountConnected = true;
        var preferred = preferredPhoneNumberId is null ? null : phoneNumbers.Data!.FirstOrDefault(p => p.Id == preferredPhoneNumberId);
        if (preferred is not null)
        {
            workspace.DefaultPhoneNumberId = preferred.Id;
            workspace.DefaultPhoneNumber = preferred.DisplayPhoneNumber;
        }
        else if (workspace.DefaultPhoneNumberId is null && phoneNumbers.Data!.Count > 0)
        {
            workspace.DefaultPhoneNumberId = phoneNumbers.Data![0].Id;
            workspace.DefaultPhoneNumber = phoneNumbers.Data![0].DisplayPhoneNumber;
        }

        workspace.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var message = $"Account connected. {(syncResult.Success ? $"Synced {syncResult.Data} templates." : "Template sync failed: " + syncResult.ErrorMessage)}";
        if (!subscribeResult.Success)
        {
            message += $" Webhook subscription failed: {subscribeResult.ErrorMessage}";
        }

        this.Notify(message, subscribeResult.Success && syncResult.Success ? "success" : "warning");
    }

    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterPhoneNumber(string phoneNumberId, string pin)
    {
        if (string.IsNullOrWhiteSpace(pin) || pin.Length != 6 || !pin.All(char.IsDigit))
        {
            this.Notify("Enter a 6-digit PIN.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var result = await _whatsAppService.RegisterPhoneNumberAsync(phoneNumberId, pin);
        this.Notify(result.Success
            ? "Number registered. Remember this PIN — Meta may ask for it again later (e.g. if you ever need to re-register)."
            : $"Registration failed: {result.ErrorMessage}", result.Success ? "success" : "danger");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefaultNumber(string phoneNumberId, string phoneNumber)
    {
        var workspace = await CurrentWorkspaceAsync();
        workspace.DefaultPhoneNumberId = phoneNumberId;
        workspace.DefaultPhoneNumber = phoneNumber;
        workspace.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        this.Notify("Default sending number updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTestMessage(string phoneNumber)
    {
        var result = await _whatsAppService.SendTestMessageAsync(phoneNumber, "This is a test message from Msgifly.");
        this.Notify(result.Success ? "Test message sent." : $"Failed to send: {result.ErrorMessage}", result.Success ? "success" : "danger");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "connect_account.disconnect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect()
    {
        var workspace = await CurrentWorkspaceAsync();
        workspace.IsAccountConnected = false;
        workspace.BusinessAccountId = null;
        workspace.AccessToken = null;
        workspace.DefaultPhoneNumberId = null;
        workspace.DefaultPhoneNumber = null;
        workspace.ProfilePictureUrl = null;
        workspace.ConnectionMethod = null;
        workspace.UpdatedAt = DateTime.UtcNow;
        await _db.WhatsappTemplates.ExecuteDeleteAsync();
        await _db.SaveChangesAsync();

        this.Notify("WhatsApp Business Account disconnected.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>A wa.me deep-link QR code for the connected default number.</summary>
    public async Task<IActionResult> QrCode()
    {
        var workspace = await CurrentWorkspaceAsync();
        if (string.IsNullOrWhiteSpace(workspace.DefaultPhoneNumber))
        {
            return NotFound();
        }

        var digitsOnly = new string(workspace.DefaultPhoneNumber.Where(char.IsDigit).ToArray());
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode($"https://wa.me/{digitsOnly}", QRCodeGenerator.ECCLevel.Q);
        var pngBytes = new PngByteQRCode(data).GetGraphic(10);

        return File(pngBytes, "image/png");
    }
}
