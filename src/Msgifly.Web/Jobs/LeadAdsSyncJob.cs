using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services;
using Msgifly.Web.Services.Automations;
using Msgifly.Web.Services.LeadAds;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Jobs;

/// <summary>
/// Runs periodically (registered as a Hangfire recurring job in Program.cs) — pulls new leads
/// from every workspace's connected Facebook Page's Lead Ads forms and imports them as Contacts.
/// Polling rather than a realtime leadgen webhook deliberately: it needs no second webhook
/// subscription per Page (Meta's Page-level Webhooks product is a separate setup step from the
/// WhatsApp webhook already configured), and for this use case — get leads off Instant Forms
/// into WhatsApp outreach without a manual CSV export — a few minutes of latency is a non-issue
/// next to how much it already improves on "manual" (see AutomationEngine's own
/// NewContactCreated trigger for what happens once a lead lands: same trigger the WhatsApp
/// webhook fires for a brand-new inbound contact).
/// </summary>
public class LeadAdsSyncJob
{
    private readonly ApplicationDbContext _db;
    private readonly MetaLeadAdsService _leadAdsService;
    private readonly AutomationEngine _automationEngine;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly ILogger<LeadAdsSyncJob> _logger;

    public LeadAdsSyncJob(
        ApplicationDbContext db,
        MetaLeadAdsService leadAdsService,
        AutomationEngine automationEngine,
        ICurrentWorkspaceAccessor workspaceAccessor,
        ILogger<LeadAdsSyncJob> logger)
    {
        _db = db;
        _leadAdsService = leadAdsService;
        _automationEngine = automationEngine;
        _workspaceAccessor = workspaceAccessor;
        _logger = logger;
    }

    public async Task SyncAllWorkspacesAsync()
    {
        var connectedWorkspaces = await _db.Workspaces.IgnoreQueryFilters()
            .Where(w => !w.IsArchived && w.FacebookPageId != null && w.FacebookPageAccessToken != null)
            .ToListAsync();

        foreach (var workspace in connectedWorkspaces)
        {
            _workspaceAccessor.WorkspaceId = workspace.Id;
            try
            {
                await SyncWorkspaceAsync(workspace);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lead Ads sync failed for workspace {WorkspaceId}", workspace.Id);
            }
        }
    }

    /// <summary>Fetches the Page's current form list and upserts it into LeadAdsForms — called both
    /// from here (every scheduled run) and immediately by LeadAdsController right after connecting
    /// a Page, so the admin doesn't stare at an empty form list until the next scheduled run.</summary>
    public async Task<List<LeadFormInfo>?> DiscoverFormsAsync(Workspace workspace)
    {
        var formsResult = await _leadAdsService.GetLeadFormsAsync(workspace.FacebookPageId!, workspace.FacebookPageAccessToken!);
        if (!formsResult.Success)
        {
            _logger.LogWarning("Couldn't list lead forms for workspace {WorkspaceId}: {Error}", workspace.Id, formsResult.ErrorMessage);
            return null;
        }

        await UpsertFormsAsync(workspace, formsResult.Data!);
        return formsResult.Data;
    }

    private async Task SyncWorkspaceAsync(Workspace workspace)
    {
        var forms = await DiscoverFormsAsync(workspace);
        if (forms is null)
        {
            return;
        }

        var enabledFormIds = await _db.LeadAdsForms
            .Where(f => f.IsEnabled)
            .Select(f => f.FormId)
            .ToListAsync();

        foreach (var form in forms.Where(f => enabledFormIds.Contains(f.Id)))
        {
            var leadsResult = await _leadAdsService.GetRecentLeadsAsync(form.Id, workspace.FacebookPageAccessToken!);
            if (!leadsResult.Success)
            {
                _logger.LogWarning("Couldn't fetch leads for form {FormId}: {Error}", form.Id, leadsResult.ErrorMessage);
                continue;
            }

            foreach (var lead in leadsResult.Data!)
            {
                if (await _db.LeadAdsImports.AnyAsync(l => l.MetaLeadId == lead.Id))
                {
                    continue; // already imported on a previous run
                }

                var contact = await ImportLeadAsContactAsync(workspace, lead);
                _db.LeadAdsImports.Add(new LeadAdsImport
                {
                    WorkspaceId = workspace.Id,
                    MetaLeadId = lead.Id,
                    FormId = form.Id,
                    ContactId = contact?.Id,
                });
                await _db.SaveChangesAsync();

                if (contact is not null)
                {
                    // Fire both: NewContactCreated for anyone whose automations already treat
                    // every fresh contact the same regardless of source, and the more specific
                    // FacebookLeadReceived for "follow up instantly on ad leads" automations.
                    await _automationEngine.RunForTriggerAsync(AutomationTriggerType.NewContactCreated, contact.Id, new AutomationContext());
                    await _automationEngine.RunForTriggerAsync(AutomationTriggerType.FacebookLeadReceived, contact.Id, new AutomationContext());
                }
            }
        }
    }

    private async Task UpsertFormsAsync(Workspace workspace, List<LeadFormInfo> forms)
    {
        var existingByFormId = await _db.LeadAdsForms.ToDictionaryAsync(f => f.FormId);

        foreach (var form in forms)
        {
            if (existingByFormId.TryGetValue(form.Id, out var row))
            {
                row.FormName = form.Name;
                row.Status = form.Status;
                row.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.LeadAdsForms.Add(new LeadAdsForm
                {
                    WorkspaceId = workspace.Id,
                    FormId = form.Id,
                    FormName = form.Name,
                    Status = form.Status,
                    IsEnabled = false,
                });
            }
        }

        await _db.SaveChangesAsync();
    }

    private async Task<Contact?> ImportLeadAsContactAsync(Workspace workspace, LeadInfo lead)
    {
        var phone = FirstValue(lead, "phone_number") ?? FirstValue(lead, "phone");
        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogWarning("Lead {LeadId} has no phone number field — skipping, WhatsApp outreach needs one.", lead.Id);
            return null;
        }

        var normalized = PhoneNumberNormalizer.Normalize(phone);
        var existing = await _db.Contacts.FirstOrDefaultAsync(c => c.Phone == phone || c.Phone == normalized || c.Phone == "+" + normalized);
        if (existing is not null)
        {
            return existing; // same person already a Contact (e.g. messaged in before) — don't duplicate, just note the lead
        }

        var sourceId = await GetOrCreateLeadAdsSourceIdAsync(workspace);
        var statusId = await _db.Statuses.Where(s => s.IsDefault).Select(s => (int?)s.Id).FirstOrDefaultAsync()
            ?? await _db.Statuses.Select(s => (int?)s.Id).FirstOrDefaultAsync();
        if (statusId is null)
        {
            _logger.LogWarning("Workspace {WorkspaceId} has no Status configured — can't import lead {LeadId}.", workspace.Id, lead.Id);
            return null;
        }

        var fullName = FirstValue(lead, "full_name") ?? FirstValue(lead, "name")
            ?? string.Join(' ', new[] { FirstValue(lead, "first_name"), FirstValue(lead, "last_name") }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var nameParts = string.IsNullOrWhiteSpace(fullName) ? [phone] : fullName.Split(' ', 2);

        var contact = new Contact
        {
            WorkspaceId = workspace.Id,
            FirstName = nameParts[0],
            LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
            Phone = normalized,
            Email = FirstValue(lead, "email"),
            Type = ContactType.Lead,
            StatusId = statusId.Value,
            SourceId = sourceId,
            IsEnabled = true,
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();
        return contact;
    }

    private async Task<int> GetOrCreateLeadAdsSourceIdAsync(Workspace workspace)
    {
        var existing = await _db.Sources.FirstOrDefaultAsync(s => s.Name == "Facebook Lead Ads");
        if (existing is not null)
        {
            return existing.Id;
        }

        var source = new Source { WorkspaceId = workspace.Id, Name = "Facebook Lead Ads" };
        _db.Sources.Add(source);
        await _db.SaveChangesAsync();
        return source.Id;
    }

    private static string? FirstValue(LeadInfo lead, string fieldName) =>
        lead.Fields.TryGetValue(fieldName, out var values) ? values.FirstOrDefault() : null;
}
