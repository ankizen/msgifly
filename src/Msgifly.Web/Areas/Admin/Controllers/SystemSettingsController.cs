using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Msgifly.Web.Extensions;
using Msgifly.Web.Services.Settings;
using Msgifly.Web.Services.Tracking;

namespace Msgifly.Web.Areas.Admin.Controllers;

/// <summary>
/// Global, infrastructure-level settings no workspace owner should ever see or edit — currently
/// just the Coolify API credentials CoolifyDomainService uses to self-register a workspace's
/// tracking domain. Same MasterAdminOnly gate as ApiKeysController: this token can trigger
/// deployments of the app itself, too much power to delegate through the normal permission system.
/// </summary>
[Area("Admin")]
[Authorize(Policy = "MasterAdminOnly")]
public class SystemSettingsController : Controller
{
    private readonly ISettingsService _settingsService;

    public SystemSettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<IActionResult> Coolify()
    {
        var settings = await _settingsService.GetAsync<CoolifyIntegrationSettings>(nameof(CoolifyIntegrationSettings));
        return View(settings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCoolify(string? baseUrl, string? apiToken, string? applicationUuid, string? composeServiceName, string? requiredDomainsCsv)
    {
        var settings = new CoolifyIntegrationSettings
        {
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://coolify.swarnapp.com" : baseUrl.Trim(),
            ApiToken = string.IsNullOrWhiteSpace(apiToken) ? null : apiToken.Trim(),
            ApplicationUuid = string.IsNullOrWhiteSpace(applicationUuid) ? null : applicationUuid.Trim(),
            ComposeServiceName = string.IsNullOrWhiteSpace(composeServiceName) ? "web" : composeServiceName.Trim(),
            RequiredDomains = (requiredDomainsCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
        };

        await _settingsService.SaveAsync(nameof(CoolifyIntegrationSettings), settings);
        this.Notify("Coolify integration settings saved.");
        return RedirectToAction(nameof(Coolify));
    }
}
