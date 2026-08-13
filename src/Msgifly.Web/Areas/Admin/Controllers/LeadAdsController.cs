using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Jobs;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.Automations;
using Msgifly.Web.Services.LeadAds;
using Msgifly.Web.Services.Settings;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

/// <summary>
/// Connects the current Workspace's Facebook Page for Lead Ads (Instant Forms) auto-import. A
/// separate Facebook Login flow from WABA's Embedded Signup (see lead-ads.js) — Pages and
/// leads_retrieval aren't part of the Embedded Signup configuration's permission grant, so this
/// asks for its own scopes and gets its own (page-scoped) access token, stored independently on
/// Workspace.FacebookPageId/Name/AccessToken.
/// </summary>
[Area("Admin")]
[Authorize]
public class LeadAdsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly MetaLeadAdsService _leadAdsService;
    private readonly ISettingsService _settingsService;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly LeadAdsSyncJob _leadAdsSyncJob;

    public LeadAdsController(
        ApplicationDbContext db,
        MetaLeadAdsService leadAdsService,
        ISettingsService settingsService,
        ICurrentWorkspaceAccessor workspaceAccessor,
        IBackgroundJobClient backgroundJobClient,
        LeadAdsSyncJob leadAdsSyncJob)
    {
        _db = db;
        _leadAdsService = leadAdsService;
        _settingsService = settingsService;
        _workspaceAccessor = workspaceAccessor;
        _backgroundJobClient = backgroundJobClient;
        _leadAdsSyncJob = leadAdsSyncJob;
    }

    private Task<Workspace> CurrentWorkspaceAsync() => _db.Workspaces.FirstAsync(w => w.Id == _workspaceAccessor.WorkspaceId);

    [Authorize(Policy = "connect_account.view")]
    public async Task<IActionResult> Index()
    {
        var workspace = await CurrentWorkspaceAsync();
        var importedCount = await _db.LeadAdsImports.CountAsync();
        var recentImports = await _db.LeadAdsImports
            .OrderByDescending(l => l.ImportedAt)
            .Take(10)
            .Select(l => new { l.MetaLeadId, l.ImportedAt, ContactName = _db.Contacts.Where(c => c.Id == l.ContactId).Select(c => c.FirstName + " " + c.LastName).FirstOrDefault() })
            .ToListAsync();

        var metaApp = await _settingsService.GetAsync<MetaAppSettings>(nameof(MetaAppSettings));
        var forms = await _db.LeadAdsForms.OrderByDescending(f => f.FormCreatedTime ?? f.CreatedAt).ToListAsync();
        var formImportCounts = await _db.LeadAdsImports
            .GroupBy(l => l.FormId)
            .Select(g => new { FormId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.FormId, g => g.Count);

        ViewData["Workspace"] = workspace;
        ViewData["ImportedCount"] = importedCount;
        ViewData["RecentImports"] = recentImports;
        ViewData["FormImportCounts"] = formImportCounts;
        ViewData["FacebookAppId"] = metaApp.FacebookAppId;
        ViewData["ApiVersion"] = metaApp.ApiVersion;
        ViewData["Forms"] = forms;
        ViewData["FormAutomations"] = await BuildFormAutomationLookupAsync();
        return View();
    }

    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChoosePage(string userAccessToken)
    {
        if (string.IsNullOrWhiteSpace(userAccessToken))
        {
            this.Notify("Facebook login didn't return a token — try again.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var result = await _leadAdsService.GetUserPagesAsync(userAccessToken);
        if (!result.Success)
        {
            this.Notify($"Couldn't list your Facebook Pages: {result.ErrorMessage}", "danger");
            return RedirectToAction(nameof(Index));
        }

        if (result.Data!.Count == 0)
        {
            this.Notify("No Facebook Pages found for this account — you need to be an admin of the Page running your Lead Ads.", "warning");
            return RedirectToAction(nameof(Index));
        }

        return View("ChoosePage", result.Data);
    }

    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConnectPage(string pageId, string pageName, string pageAccessToken)
    {
        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(pageAccessToken))
        {
            this.Notify("Missing page details — try connecting again.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var workspace = await CurrentWorkspaceAsync();
        workspace.FacebookPageId = pageId;
        workspace.FacebookPageName = pageName;
        workspace.FacebookPageAccessToken = pageAccessToken;
        workspace.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var forms = await _leadAdsSyncJob.DiscoverFormsAsync(workspace);
        var formNote = forms is { Count: > 0 }
            ? $" Found {forms.Count} form(s) below — turn on the ones you want synced."
            : " No Instant Forms found on this Page yet.";
        this.Notify($"Connected \"{pageName}\".{formNote}");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleForm(int id)
    {
        var form = await _db.LeadAdsForms.FirstOrDefaultAsync(f => f.Id == id);
        if (form is null)
        {
            return NotFound();
        }

        form.IsEnabled = !form.IsEnabled;
        form.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        this.Notify(form.IsEnabled
            ? $"\"{form.FormName}\" will now sync new leads."
            : $"\"{form.FormName}\" sync turned off.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public IActionResult SyncNow()
    {
        _backgroundJobClient.Enqueue<LeadAdsSyncJob>(job => job.SyncAllWorkspacesAsync());
        this.Notify("Sync started — refresh in a few seconds to see new leads.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "connect_account.disconnect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect()
    {
        var workspace = await CurrentWorkspaceAsync();
        workspace.FacebookPageId = null;
        workspace.FacebookPageName = null;
        workspace.FacebookPageAccessToken = null;
        workspace.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        this.Notify("Facebook Page disconnected. Already-imported leads stay as Contacts.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Maps each form to the FacebookLeadReceived automation scoped to it (if any), plus
    /// a special "" key for a catch-all automation with no form restriction — lets the forms list
    /// show whether a lead landing on this form will actually trigger anything, instead of the
    /// admin having to cross-reference the Automations screen by hand.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);

    private async Task<Dictionary<string, FormAutomationInfo>> BuildFormAutomationLookupAsync()
    {
        var candidates = await _db.Automations.AsNoTracking()
            .Where(a => a.TriggerType == AutomationTriggerType.FacebookLeadReceived && a.IsActive)
            .Select(a => new { a.Id, a.Name, a.TriggerConfigJson })
            .ToListAsync();

        var lookup = new Dictionary<string, FormAutomationInfo>();
        foreach (var candidate in candidates)
        {
            string formId;
            try
            {
                // TriggerConfigJson is always written camelCase (JsonSerializerOptions.Web, see
                // AutomationsController.BuildTriggerConfigJson) — deserializing without the same
                // options here silently no-ops the property match ("formId" != "FormId" under
                // System.Text.Json's default case-sensitive comparison) and every automation
                // reads back as the catch-all, which is exactly the bug this line fixes.
                formId = JsonSerializer.Deserialize<FacebookLeadFormTriggerConfig>(candidate.TriggerConfigJson, JsonOptions)?.FormId ?? string.Empty;
            }
            catch (JsonException)
            {
                continue;
            }

            // First match wins if two automations somehow target the same form/catch-all — a
            // deliberately simple tie-break since the UI's job here is discoverability, not
            // enforcing exactly-one-automation-per-form.
            lookup.TryAdd(formId, new FormAutomationInfo(candidate.Id, candidate.Name));
        }

        return lookup;
    }
}

public record FormAutomationInfo(int Id, string Name);
