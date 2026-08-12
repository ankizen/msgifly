using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.Settings;
using Msgifly.Web.Services.WhatsApp;
using QRCoder;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class WabaController : Controller
{
    private readonly ISettingsService _settingsService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ApplicationDbContext _db;

    public WabaController(ISettingsService settingsService, IWhatsAppService whatsAppService, ApplicationDbContext db)
    {
        _settingsService = settingsService;
        _whatsAppService = whatsAppService;
        _db = db;
    }

    [Authorize(Policy = "connect_account.view")]
    public async Task<IActionResult> Index()
    {
        var settings = await _settingsService.GetAsync<WhatsAppSettings>(nameof(WhatsAppSettings));

        var model = new WabaIndexViewModel
        {
            IsWebhookConnected = settings.IsWebhookConnected,
            IsAccountConnected = settings.IsAccountConnected,
            WebhookUrl = $"{Request.Scheme}://{Request.Host}/whatsapp/webhook",
            WebhookVerifyToken = settings.WebhookVerifyToken,
            DefaultPhoneNumberId = settings.DefaultPhoneNumberId,
            DefaultPhoneNumber = settings.DefaultPhoneNumber,
        };

        if (settings.IsAccountConnected)
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

            if (!string.IsNullOrWhiteSpace(settings.DefaultPhoneNumberId))
            {
                var profile = await _whatsAppService.GetBusinessProfileAsync(settings.DefaultPhoneNumberId);
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
        var settings = await _settingsService.GetAsync<WhatsAppSettings>(nameof(WhatsAppSettings));
        if (string.IsNullOrWhiteSpace(settings.DefaultPhoneNumberId))
        {
            this.Notify("Choose a default number first.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var result = await _whatsAppService.UpdateBusinessProfileAsync(settings.DefaultPhoneNumberId, new BusinessProfileUpdateRequest
        {
            About = form.About,
            Email = form.Email,
            Website = form.Website,
            Vertical = form.Vertical,
        });

        this.Notify(result.Success ? "Business profile updated." : $"Couldn't update profile: {result.ErrorMessage}", result.Success ? "success" : "danger");
        return RedirectToAction(nameof(Index));
    }

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

        var settings = await _settingsService.GetAsync<WhatsAppSettings>(nameof(WhatsAppSettings));
        settings.FacebookAppId = form.FacebookAppId;
        settings.FacebookAppSecret = form.FacebookAppSecret;
        settings.WebhookVerifyToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        settings.IsWebhookConnected = true;
        await _settingsService.SaveAsync(nameof(WhatsAppSettings), settings);

        this.Notify("Webhook app registered. Add the webhook URL and verify token to your Meta App's WhatsApp configuration, then connect your Business Account below.");
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

        var settings = await _settingsService.GetAsync<WhatsAppSettings>(nameof(WhatsAppSettings));
        settings.BusinessAccountId = form.BusinessAccountId;
        settings.AccessToken = form.AccessToken;
        await _settingsService.SaveAsync(nameof(WhatsAppSettings), settings);

        var phoneNumbers = await _whatsAppService.GetPhoneNumbersAsync();
        if (!phoneNumbers.Success)
        {
            this.Notify($"Couldn't connect: {phoneNumbers.ErrorMessage}", "danger");
            return RedirectToAction(nameof(Index));
        }

        var syncResult = await _whatsAppService.SyncTemplatesAsync();
        var subscribeResult = await _whatsAppService.SubscribeWebhookAsync();

        settings = await _settingsService.GetAsync<WhatsAppSettings>(nameof(WhatsAppSettings));
        settings.IsAccountConnected = true;
        if (settings.DefaultPhoneNumberId is null && phoneNumbers.Data!.Count > 0)
        {
            settings.DefaultPhoneNumberId = phoneNumbers.Data![0].Id;
            settings.DefaultPhoneNumber = phoneNumbers.Data![0].DisplayPhoneNumber;
        }

        await _settingsService.SaveAsync(nameof(WhatsAppSettings), settings);

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
        var settings = await _settingsService.GetAsync<WhatsAppSettings>(nameof(WhatsAppSettings));
        settings.DefaultPhoneNumberId = phoneNumberId;
        settings.DefaultPhoneNumber = phoneNumber;
        await _settingsService.SaveAsync(nameof(WhatsAppSettings), settings);

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
        await _settingsService.SaveAsync(nameof(WhatsAppSettings), new WhatsAppSettings());
        await _db.WhatsappTemplates.ExecuteDeleteAsync();

        this.Notify("WhatsApp Business Account disconnected.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>A wa.me deep-link QR code for the connected default number.</summary>
    public async Task<IActionResult> QrCode()
    {
        var settings = await _settingsService.GetAsync<WhatsAppSettings>(nameof(WhatsAppSettings));
        if (string.IsNullOrWhiteSpace(settings.DefaultPhoneNumber))
        {
            return NotFound();
        }

        var digitsOnly = new string(settings.DefaultPhoneNumber.Where(char.IsDigit).ToArray());
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode($"https://wa.me/{digitsOnly}", QRCodeGenerator.ECCLevel.Q);
        var pngBytes = new PngByteQRCode(data).GetGraphic(10);

        return File(pngBytes, "image/png");
    }
}
