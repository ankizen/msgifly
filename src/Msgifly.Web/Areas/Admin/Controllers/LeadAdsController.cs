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
using Msgifly.Web.Models.ViewModels;
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

        // LeadAdsImports is an append-only dedup ledger (see its own doc comment) — it's never
        // pruned when a Contact is later deleted, so counting it directly showed a stale "N
        // imported" even after every one of those contacts was deleted. Count only imports whose
        // Contact still exists, so this reflects what's actually in the CRM right now.
        var importedCount = await _db.LeadAdsImports
            .Where(l => l.ContactId != null && _db.Contacts.Any(c => c.Id == l.ContactId))
            .CountAsync();

        var recentImports = await _db.LeadAdsImports
            .OrderByDescending(l => l.ImportedAt)
            .Take(10)
            .Select(l => new
            {
                l.MetaLeadId,
                l.ImportedAt,
                ContactName = _db.Contacts.Where(c => c.Id == l.ContactId).Select(c => c.FirstName + " " + c.LastName).FirstOrDefault(),
                WasSkipped = l.ContactId == null,
            })
            .ToListAsync();

        var metaApp = await _settingsService.GetAsync<MetaAppSettings>(nameof(MetaAppSettings));
        var forms = await _db.LeadAdsForms.OrderByDescending(f => f.FormCreatedTime ?? f.CreatedAt).ToListAsync();
        var formImportCounts = await _db.LeadAdsImports
            .Where(l => l.ContactId != null && _db.Contacts.Any(c => c.Id == l.ContactId))
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
        ViewData["WebhookUrl"] = $"{Request.Scheme}://{Request.Host}/whatsapp/webhook";
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

        // Exchange for a long-lived user token before deriving Page tokens from it — a Page token
        // requested with the short-lived token FB.login() hands back inherits its ~1-2 hour
        // lifetime and quietly stops working a couple hours after connecting. Falls back to the
        // short-lived token on exchange failure rather than blocking the connect entirely; it'll
        // just have the same short lifetime as before this existed.
        var exchangeResult = await _leadAdsService.ExchangeForLongLivedTokenAsync(userAccessToken);
        var longLivedToken = exchangeResult.Success ? exchangeResult.Data! : userAccessToken;

        var result = await _leadAdsService.GetUserPagesAsync(longLivedToken);
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

        // Best-effort: makes new leads arrive within seconds instead of waiting on the next
        // scheduled poll. Needs the Meta App's own Webhooks product to also have "page"/"leadgen"
        // enabled against the same callback URL used for WhatsApp — a one-time Dashboard step we
        // can't drive via API, so a failure here isn't fatal, it just falls back to polling-only.
        var subscribeResult = await _leadAdsService.SubscribePageWebhookAsync(pageId, pageAccessToken);
        var webhookNote = subscribeResult.Success
            ? " Realtime lead sync is on."
            : " Realtime lead sync couldn't be enabled (leads will still sync every minute) — check that this Page is added under the Meta App's Webhooks product.";

        this.Notify($"Connected \"{pageName}\".{formNote}{webhookNote}");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// The ad-spend-to-engagement funnel for one form: leads imported, and — joining
    /// Contact.LeadAdsFormId to Chat by phone number, then to ChatMessage — how many outbound
    /// template sends actually reached, were read by, and got clicked by those leads. Answers "is
    /// this form's ad spend working" directly, which the plain per-form import count above doesn't.
    /// </summary>
    [Authorize(Policy = "connect_account.view")]
    public async Task<IActionResult> Report(string formId)
    {
        var form = await _db.LeadAdsForms.AsNoTracking().FirstOrDefaultAsync(f => f.FormId == formId);
        if (form is null)
        {
            return NotFound();
        }

        var leadsImported = await _db.Contacts.AsNoTracking().CountAsync(c => c.LeadAdsFormId == formId);

        var phones = await _db.Contacts.AsNoTracking()
            .Where(c => c.LeadAdsFormId == formId)
            .Select(c => c.Phone)
            .ToListAsync();

        // Outbound template sends only (SenderId != the lead's own number) — this is what tells
        // us whether the welcome message (and any follow-ups) actually reached these leads, not
        // just that they were imported.
        var counts = await _db.ChatMessages.AsNoTracking()
            .Where(m => phones.Contains(m.Chat.ReceiverId) && m.TemplateName != null && m.SenderId != m.Chat.ReceiverId)
            .GroupBy(m => 1)
            .Select(g => new
            {
                Sent = g.Count(),
                Delivered = g.Count(m => m.Status == MessageDeliveryStatus.Delivered || m.Status == MessageDeliveryStatus.Read),
                Read = g.Count(m => m.Status == MessageDeliveryStatus.Read),
                Failed = g.Count(m => m.Status == MessageDeliveryStatus.Failed),
                Clicked = g.Count(m => m.Clicked),
            })
            .FirstOrDefaultAsync();

        // Projected into an anonymous type first, then mapped to the record in memory — EF Core
        // can't translate a record's positional constructor combined with multiple aggregate
        // Count() calls directly inside one GroupBy projection (same reason TemplatesController's
        // Report action does its own campaignCounts/chatCounts this way).
        var byTemplateRaw = await _db.ChatMessages.AsNoTracking()
            .Where(m => phones.Contains(m.Chat.ReceiverId) && m.TemplateName != null && m.SenderId != m.Chat.ReceiverId)
            .GroupBy(m => m.TemplateName)
            .Select(g => new { TemplateName = g.Key!, Sent = g.Count(), Clicked = g.Count(m => m.Clicked) })
            .OrderByDescending(t => t.Sent)
            .ToListAsync();
        var byTemplate = byTemplateRaw.Select(t => new LeadAdsFormTemplateStat(t.TemplateName, t.Sent, t.Clicked)).ToList();

        var model = new LeadAdsFormReportViewModel
        {
            FormId = form.FormId,
            FormName = form.FormName,
            LeadsImported = leadsImported,
            TemplatesSent = counts?.Sent ?? 0,
            DeliveredCount = counts?.Delivered ?? 0,
            ReadCount = counts?.Read ?? 0,
            ClickedCount = counts?.Clicked ?? 0,
            FailedCount = counts?.Failed ?? 0,
            ByTemplate = byTemplate,
        };

        return View(model);
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

    /// <summary>Full re-sync of one form on demand — unlike the routine per-minute poll, this
    /// brings back leads whose Contact was since deleted from the CRM (see
    /// LeadAdsSyncJob.ManualSyncFormAsync's own doc comment for why that's safe to do only here,
    /// not on the automatic paths). Runs as a background job since "all leads" on a form with a
    /// lot of history can take a while.</summary>
    [HttpPost]
    [Authorize(Policy = "connect_account.connect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncForm(string formId, bool allLeads, DateTime? fromDate)
    {
        var form = await _db.LeadAdsForms.FirstOrDefaultAsync(f => f.FormId == formId);
        if (form is null)
        {
            return NotFound();
        }

        DateTime? sinceUtc = allLeads || fromDate is null ? null : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
        _backgroundJobClient.Enqueue<LeadAdsSyncJob>(job => job.ManualSyncFormAsync(_workspaceAccessor.WorkspaceId!.Value, formId, sinceUtc));

        this.Notify($"Syncing \"{form.FormName}\"{(allLeads ? " — full history" : $" from {sinceUtc:dd MMM yyyy}")}. This can take a minute for a form with a lot of leads — refresh shortly.");
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
