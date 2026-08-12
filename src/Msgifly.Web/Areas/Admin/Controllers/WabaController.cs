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
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public WabaController(ISettingsService settingsService, IWhatsAppService whatsAppService, ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _settingsService = settingsService;
        _whatsAppService = whatsAppService;
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
                        Email = profile.Data.Email,
                        Website = profile.Data.Websites,
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
            Email = form.Email,
            Website = form.Website,
            Vertical = form.Vertical,
        });

        this.Notify(result.Success ? "Business profile updated." : $"Couldn't update profile: {result.ErrorMessage}", result.Success ? "success" : "danger");
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
        settings.WebhookVerifyToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
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

        var phoneNumbers = await _whatsAppService.GetPhoneNumbersAsync();
        if (!phoneNumbers.Success)
        {
            this.Notify($"Couldn't connect: {phoneNumbers.ErrorMessage}", "danger");
            return RedirectToAction(nameof(Index));
        }

        var syncResult = await _whatsAppService.SyncTemplatesAsync();
        var subscribeResult = await _whatsAppService.SubscribeWebhookAsync();

        workspace.IsAccountConnected = true;
        if (workspace.DefaultPhoneNumberId is null && phoneNumbers.Data!.Count > 0)
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
