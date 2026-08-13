using System.Text.Json;
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
/// The primary path is now realtime: WhatsAppWebhookController receives Meta's leadgen webhook
/// the moment someone submits a form and calls ImportSingleLeadAsync directly. This poll stays in
/// place as a safety net (a missed/delayed webhook delivery, or a lead that arrived before the
/// form's local schema row existed yet) — same dedup-by-MetaLeadId either path uses, so whichever
/// gets to a given lead first is a no-op for the other (see AutomationEngine's own
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
        // Cheap and idempotent — also self-heals workspaces connected before realtime sync
        // existed (or where the subscription silently lapsed) without needing them to
        // disconnect/reconnect the Page. Failures are expected until the Meta App Dashboard's
        // own "page"/"leadgen" webhook field is enabled, so this stays log-only, not fatal.
        var subscribeResult = await _leadAdsService.SubscribePageWebhookAsync(workspace.FacebookPageId!, workspace.FacebookPageAccessToken!);
        if (!subscribeResult.Success)
        {
            _logger.LogDebug("Realtime lead webhook subscription not active for workspace {WorkspaceId}: {Error}", workspace.Id, subscribeResult.ErrorMessage);
        }

        var forms = await DiscoverFormsAsync(workspace);
        if (forms is null)
        {
            return;
        }

        var enabledForms = await _db.LeadAdsForms.Where(f => f.IsEnabled).ToListAsync();
        var enabledFormIds = enabledForms.Select(f => f.FormId).ToHashSet();

        foreach (var form in forms.Where(f => enabledFormIds.Contains(f.Id)))
        {
            var localForm = enabledForms.First(f => f.FormId == form.Id);
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
                    continue; // already imported on a previous run (or by the realtime webhook already)
                }

                await ImportAndTrackLeadAsync(workspace, lead, localForm);
            }
        }
    }

    /// <summary>
    /// Entry point for the realtime leadgen webhook (see WhatsAppWebhookController) — Meta's push
    /// notification only carries the leadgen_id, not the answers, so this fetches the one lead and
    /// runs it through the same import path the polling job uses. This is what makes a lead show up
    /// within seconds instead of waiting on the next scheduled poll; the poll stays in place as a
    /// safety net for any webhook delivery that's missed or arrives before a form's local schema
    /// row exists yet.
    /// </summary>
    public async Task ImportSingleLeadAsync(Workspace workspace, string formId, string leadgenId)
    {
        if (string.IsNullOrEmpty(workspace.FacebookPageAccessToken))
        {
            _logger.LogWarning("Leadgen webhook for workspace {WorkspaceId} — no Page access token on file, ignoring.", workspace.Id);
            return;
        }

        if (await _db.LeadAdsImports.AnyAsync(l => l.MetaLeadId == leadgenId))
        {
            return; // already imported (duplicate webhook delivery, or the poller already got it)
        }

        var form = await _db.LeadAdsForms.FirstOrDefaultAsync(f => f.FormId == formId && f.IsEnabled);
        if (form is null)
        {
            _logger.LogInformation("Leadgen webhook for form {FormId} — form unknown locally or sync not enabled for it, ignoring.", formId);
            return;
        }

        var leadResult = await _leadAdsService.GetLeadByIdAsync(leadgenId, workspace.FacebookPageAccessToken!);
        if (!leadResult.Success)
        {
            _logger.LogWarning("Couldn't fetch lead {LeadId} from the leadgen webhook: {Error}", leadgenId, leadResult.ErrorMessage);
            return;
        }

        await ImportAndTrackLeadAsync(workspace, leadResult.Data!, form);
    }

    /// <summary>
    /// Entry point for the "Sync this form" manual action (LeadAdsController.SyncForm) — runs in
    /// the background via Hangfire since a full-history pull can take a while. Unlike the
    /// automatic poll and the webhook path above, this deliberately skips the
    /// LeadAdsImports-ledger existence check — it relies only on ImportLeadAsContactAsync's own
    /// phone-number dedup. That's what lets a lead whose Contact was since deleted come back on
    /// an explicit, user-triggered re-sync, while the automatic paths stay conservative and never
    /// silently resurrect a deleted contact on their own.
    /// </summary>
    public async Task ManualSyncFormAsync(int workspaceId, string formId, DateTime? sinceUtc)
    {
        var workspace = await _db.Workspaces.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.Id == workspaceId);
        if (workspace is null || string.IsNullOrEmpty(workspace.FacebookPageAccessToken))
        {
            _logger.LogWarning("Manual Lead Ads sync requested for workspace {WorkspaceId} but it has no Page connected.", workspaceId);
            return;
        }

        _workspaceAccessor.WorkspaceId = workspaceId;

        var form = await _db.LeadAdsForms.FirstOrDefaultAsync(f => f.FormId == formId);
        if (form is null)
        {
            _logger.LogWarning("Manual Lead Ads sync requested for unknown form {FormId}", formId);
            return;
        }

        var leadsResult = await _leadAdsService.GetAllLeadsAsync(formId, workspace.FacebookPageAccessToken, sinceUtc);
        if (!leadsResult.Success)
        {
            _logger.LogWarning("Manual sync couldn't fetch leads for form {FormId}: {Error}", formId, leadsResult.ErrorMessage);
            return;
        }

        foreach (var lead in leadsResult.Data!)
        {
            try
            {
                await ImportAndTrackLeadAsync(workspace, lead, form);
            }
            catch (Exception ex)
            {
                // One bad lead must never abort the rest of the batch — that's exactly what
                // happened before the upsert fix in ImportAndTrackLeadAsync (a duplicate-key
                // exception on one lead silently dropped every lead after it in the loop).
                _logger.LogError(ex, "Manual sync failed to import lead {LeadId} for form {FormId} — continuing with the rest.", lead.Id, formId);
            }
        }
    }

    private async Task ImportAndTrackLeadAsync(Workspace workspace, LeadInfo lead, LeadAdsForm form)
    {
        var contact = await ImportLeadAsContactAsync(workspace, lead, form);

        // Upsert, not blind-insert: LeadAdsImports has a unique index on (WorkspaceId,
        // MetaLeadId), so re-processing a lead that's already in the ledger (which
        // ManualSyncFormAsync deliberately does, to bring back leads whose Contact was
        // deleted) would otherwise throw a duplicate-key DbUpdateException and abort the
        // whole batch partway through — exactly what happened before this fix.
        var existingImport = await _db.LeadAdsImports.FirstOrDefaultAsync(l => l.WorkspaceId == workspace.Id && l.MetaLeadId == lead.Id);
        if (existingImport is not null)
        {
            existingImport.ContactId = contact?.Id;
            existingImport.ImportedAt = DateTime.UtcNow;
        }
        else
        {
            _db.LeadAdsImports.Add(new LeadAdsImport
            {
                WorkspaceId = workspace.Id,
                MetaLeadId = lead.Id,
                FormId = form.FormId,
                ContactId = contact?.Id,
            });
        }

        await _db.SaveChangesAsync();

        if (contact is not null)
        {
            // Fire both: NewContactCreated for anyone whose automations already treat every fresh
            // contact the same regardless of source, and the more specific FacebookLeadReceived
            // for "follow up instantly on ad leads" automations — carrying the form id so an
            // automation scoped to one specific form (not every form on the Page) only fires for
            // leads that actually came from it.
            await _automationEngine.RunForTriggerAsync(AutomationTriggerType.NewContactCreated, contact.Id, new AutomationContext());
            await _automationEngine.RunForTriggerAsync(AutomationTriggerType.FacebookLeadReceived, contact.Id, new AutomationContext { LeadFormId = form.FormId });
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
                row.FormCreatedTime ??= form.CreatedTime;
                row.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                row = new LeadAdsForm
                {
                    WorkspaceId = workspace.Id,
                    FormId = form.Id,
                    FormName = form.Name,
                    Status = form.Status,
                    FormCreatedTime = form.CreatedTime,
                    IsEnabled = false,
                };
                _db.LeadAdsForms.Add(row);
            }

            // Published forms' questions don't change, so fetch this once per form rather than
            // on every 10-minute sync — a real API call per already-known form on every tick
            // would be pure waste.
            if (string.IsNullOrEmpty(row.QuestionsJson))
            {
                var questionsResult = await _leadAdsService.GetFormQuestionsAsync(form.Id, workspace.FacebookPageAccessToken!);
                if (questionsResult.Success)
                {
                    row.QuestionsJson = JsonSerializer.Serialize(questionsResult.Data);
                }
                else
                {
                    _logger.LogWarning("Couldn't fetch question schema for form {FormId}: {Error}", form.Id, questionsResult.ErrorMessage);
                }
            }
        }

        await _db.SaveChangesAsync();
    }

    private async Task<Contact?> ImportLeadAsContactAsync(Workspace workspace, LeadInfo lead, LeadAdsForm form)
    {
        var questions = ParseQuestions(form.QuestionsJson);
        var answersNote = BuildAnswersNote(lead, questions);

        var phone = ResolveByType(lead, questions, PhoneTypes) ?? FirstValue(lead, "phone_number") ?? FirstValue(lead, "phone");
        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogWarning("Lead {LeadId} on form \"{FormName}\" has no phone number field — skipping, WhatsApp outreach needs one.", lead.Id, form.FormName);
            return null;
        }

        var normalized = PhoneNumberNormalizer.Normalize(phone);
        var existing = await _db.Contacts.FirstOrDefaultAsync(c => c.Phone == phone || c.Phone == normalized || c.Phone == "+" + normalized);
        if (existing is not null)
        {
            // Same person already a Contact (e.g. messaged in before, or submitted this form
            // previously) — don't duplicate, but do log the resubmission so a rep can see renewed
            // interest instead of it vanishing silently.
            _db.ContactNotes.Add(new ContactNote
            {
                ContactId = existing.Id,
                Description = $"Submitted \"{form.FormName}\" on Facebook Lead Ads again." + (answersNote is null ? "" : "\n" + answersNote),
            });
            await _db.SaveChangesAsync();
            return existing;
        }

        var sourceId = await GetOrCreateLeadAdsSourceIdAsync(workspace);
        var statusId = await _db.Statuses.Where(s => s.IsDefault).Select(s => (int?)s.Id).FirstOrDefaultAsync()
            ?? await _db.Statuses.Select(s => (int?)s.Id).FirstOrDefaultAsync();
        if (statusId is null)
        {
            _logger.LogWarning("Workspace {WorkspaceId} has no Status configured — can't import lead {LeadId}.", workspace.Id, lead.Id);
            return null;
        }

        var fullName = ResolveByType(lead, questions, FullNameTypes)
            ?? FirstValue(lead, "full_name") ?? FirstValue(lead, "name")
            ?? string.Join(' ', new[]
            {
                ResolveByType(lead, questions, FirstNameTypes) ?? FirstValue(lead, "first_name"),
                ResolveByType(lead, questions, LastNameTypes) ?? FirstValue(lead, "last_name"),
            }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var nameParts = string.IsNullOrWhiteSpace(fullName) ? [phone] : fullName.Split(' ', 2);

        var contact = new Contact
        {
            WorkspaceId = workspace.Id,
            FirstName = nameParts[0],
            LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
            Phone = normalized,
            Email = ResolveByType(lead, questions, EmailTypes) ?? FirstValue(lead, "email"),
            City = ResolveByType(lead, questions, CityTypes) ?? FirstValue(lead, "city"),
            State = ResolveByType(lead, questions, StateTypes) ?? FirstValue(lead, "state"),
            Zip = ResolveByType(lead, questions, ZipTypes) ?? FirstValue(lead, "zip_code"),
            Company = ResolveByType(lead, questions, CompanyTypes) ?? FirstValue(lead, "company_name"),
            LeadAdsFormId = form.FormId,
            LeadAdsFormName = form.FormName,
            Type = ContactType.Lead,
            StatusId = statusId.Value,
            SourceId = sourceId,
            IsEnabled = true,
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();

        if (answersNote is not null)
        {
            _db.ContactNotes.Add(new ContactNote
            {
                ContactId = contact.Id,
                Description = $"From Facebook Lead Ads form \"{form.FormName}\":\n{answersNote}",
            });
            await _db.SaveChangesAsync();
        }

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

    private static List<LeadFormQuestion>? ParseQuestions(string? questionsJson)
    {
        if (string.IsNullOrEmpty(questionsJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<LeadFormQuestion>>(questionsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Meta's fixed per-question PII types (standard fields get a predictable field_data
    /// key of their own; CUSTOM questions don't) — matched against the form's cached schema so a
    /// lead's answers resolve correctly regardless of what its raw field_data keys happen to be.</summary>
    private static readonly string[] PhoneTypes = ["PHONE", "WHATSAPP_NUMBER", "WORK_PHONE_NUMBER"];
    private static readonly string[] EmailTypes = ["EMAIL", "WORK_EMAIL"];
    private static readonly string[] FullNameTypes = ["FULL_NAME"];
    private static readonly string[] FirstNameTypes = ["FIRST_NAME"];
    private static readonly string[] LastNameTypes = ["LAST_NAME"];
    private static readonly string[] CityTypes = ["CITY"];
    private static readonly string[] StateTypes = ["STATE"];
    private static readonly string[] ZipTypes = ["ZIP", "ZIP_CODE", "POST_CODE", "POSTAL_CODE"];
    private static readonly string[] CompanyTypes = ["COMPANY_NAME"];

    private static string? ResolveByType(LeadInfo lead, List<LeadFormQuestion>? questions, string[] types)
    {
        var key = questions?.FirstOrDefault(q => types.Contains(q.Type, StringComparer.OrdinalIgnoreCase))?.Key;
        return string.IsNullOrEmpty(key) ? null : FirstValue(lead, key);
    }

    /// <summary>The full Q&amp;A for this submission, label-mapped where a schema is cached —
    /// attached as a Contact note so nothing from the actual form (including custom questions with
    /// no dedicated Contact field) is silently dropped, and so the original submission stays
    /// visible even if the mapped fields get edited later.</summary>
    private static string? BuildAnswersNote(LeadInfo lead, List<LeadFormQuestion>? questions)
    {
        var lines = new List<string>();
        if (questions is { Count: > 0 })
        {
            foreach (var q in questions)
            {
                var value = FirstValue(lead, q.Key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    lines.Add($"{q.Label}: {value}");
                }
            }
        }
        else
        {
            foreach (var (key, values) in lead.Fields)
            {
                var value = values.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    lines.Add($"{key}: {value}");
                }
            }
        }

        return lines.Count > 0 ? string.Join('\n', lines) : null;
    }
}
