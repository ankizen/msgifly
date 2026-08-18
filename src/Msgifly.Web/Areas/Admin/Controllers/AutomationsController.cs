using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services;
using Msgifly.Web.Services.Automations;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class AutomationsController : Controller
{
    private const int PageSize = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);

    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly AutomationEngine _automationEngine;

    public AutomationsController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor, AutomationEngine automationEngine)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
        _automationEngine = automationEngine;
    }

    [Authorize(Policy = "automation.view")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var query = _db.Automations.AsNoTracking().OrderByDescending(a => a.CreatedAt);
        return View(await PagedList<Automation>.CreateAsync(query, page, PageSize));
    }

    [Authorize(Policy = "automation.view")]
    public async Task<IActionResult> Logs(int id, int page = 1)
    {
        var automation = await _db.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (automation is null)
        {
            return NotFound();
        }

        ViewData["Automation"] = automation;
        var query = _db.AutomationLogs.AsNoTracking().Where(l => l.AutomationId == id).OrderByDescending(l => l.CreatedAt);
        return View(await PagedList<AutomationLog>.CreateAsync(query, page, PageSize));
    }

    /// <summary>
    /// Run-level and per-step analytics for one automation — how many contacts have gone through
    /// it, and for each step in the tree (in the same order the canvas draws it), how far it got.
    /// Scoped to this automation's own AutomationLog rows, unlike the Templates Report page which
    /// aggregates a template globally across every automation/campaign/quick-send.
    /// </summary>
    [Authorize(Policy = "automation.view")]
    public async Task<IActionResult> Report(int id)
    {
        var automation = await _db.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (automation is null)
        {
            return NotFound();
        }

        var steps = await _db.AutomationSteps.AsNoTracking().Where(s => s.AutomationId == id).ToListAsync();
        var logs = await _db.AutomationLogs.AsNoTracking().Where(l => l.AutomationId == id).ToListAsync();

        var model = new AutomationReportViewModel
        {
            AutomationId = automation.Id,
            AutomationName = automation.Name,
            TotalRuns = logs.Count,
            CompletedRuns = logs.Count(l => l.Status == AutomationLogStatus.Success),
            WaitingRuns = logs.Count(l => l.Status == AutomationLogStatus.Partial),
            FailedRuns = logs.Count(l => l.Status == AutomationLogStatus.Failed),
        };

        var allResults = new List<AutomationStepResult>();
        foreach (var log in logs)
        {
            allResults.AddRange(SafeDeserialize<List<AutomationStepResult>>(log.StepsExecutedJson) ?? []);
        }

        var byStepId = allResults.Where(r => r.StepId is not null).ToLookup(r => r.StepId!.Value);

        // A successful SendTemplate step's Detail is "template sent (wamid...)" — pull the message
        // id back out so live delivery/read/click status (which changes after the send, via later
        // webhooks) can be looked up, rather than trusting the log's own send-time snapshot.
        var wamidPattern = new Regex(@"\(([^()]+)\)\s*$");
        var wamids = new HashSet<string>();
        foreach (var r in allResults.Where(r => r.StepType == "SendTemplate" && r.Status == "success"))
        {
            var m = wamidPattern.Match(r.Detail);
            if (m.Success)
            {
                wamids.Add(m.Groups[1].Value);
            }
        }

        var messagesByWamid = wamids.Count == 0
            ? new Dictionary<string, ChatMessage>()
            : await _db.ChatMessages.AsNoTracking()
                .Where(m => m.WhatsappMessageId != null && wamids.Contains(m.WhatsappMessageId))
                .ToDictionaryAsync(m => m.WhatsappMessageId!);

        foreach (var (step, depth) in WalkInTreeOrder(steps, null, null, 0))
        {
            var results = byStepId[step.Id].ToList();
            var report = new AutomationStepReport
            {
                StepId = step.Id,
                StepType = step.StepType.ToString(),
                Branch = step.Branch ?? string.Empty,
                Depth = depth,
                Label = DescribeStep(step),
                ReachedCount = results.Count,
                SuccessCount = results.Count(r => r.Status == "success"),
                FailedCount = results.Count(r => r.Status == "failed"),
                FailureReasons = [.. results.Where(r => r.Status == "failed")
                    .GroupBy(r => r.Detail)
                    .Select(g => new AutomationFailureReason(g.Key, g.Count()))
                    .OrderByDescending(f => f.Count)],
            };

            if (step.StepType == AutomationStepType.SendTemplate)
            {
                report.IsSendTemplate = true;
                foreach (var r in results.Where(r => r.Status == "success"))
                {
                    var m = wamidPattern.Match(r.Detail);
                    if (m.Success && messagesByWamid.TryGetValue(m.Groups[1].Value, out var msg))
                    {
                        if (msg.Status is MessageDeliveryStatus.Delivered or MessageDeliveryStatus.Read) report.DeliveredCount++;
                        if (msg.Status == MessageDeliveryStatus.Read) report.ReadCount++;
                        if (msg.Clicked) report.ClickedCount++;
                    }
                }
            }
            else if (step.StepType == AutomationStepType.Condition)
            {
                report.IsCondition = true;
                report.YesCount = results.Count(r => r.Detail == "branch=Yes");
                report.NoCount = results.Count(r => r.Detail == "branch=No");
            }

            model.Steps.Add(report);
        }

        return View(model);
    }

    /// <summary>Walks the flattened AutomationStep rows in the same root-then-Yes-then-No order
    /// the canvas draws them (mirrors AutomationFormViewModel.BuildTree's recursion), but yields a
    /// flat (step, depth) sequence instead of nested JSON — depth drives the report's indentation.</summary>
    private static IEnumerable<(AutomationStep Step, int Depth)> WalkInTreeOrder(List<AutomationStep> allSteps, int? parentId, string? branch, int depth)
    {
        var scoped = allSteps.Where(s => s.ParentStepId == parentId && s.Branch == branch).OrderBy(s => s.Position);
        foreach (var step in scoped)
        {
            yield return (step, depth);
            if (step.StepType == AutomationStepType.Condition)
            {
                foreach (var x in WalkInTreeOrder(allSteps, step.Id, "Yes", depth + 1)) yield return x;
                foreach (var x in WalkInTreeOrder(allSteps, step.Id, "No", depth + 1)) yield return x;
            }
        }
    }

    private static string DescribeStep(AutomationStep step)
    {
        switch (step.StepType)
        {
            case AutomationStepType.SendTemplate:
                return SafeDeserialize<SendTemplateStepConfig>(step.StepConfigJson)?.TemplateName is { Length: > 0 } name ? name : "(unknown template)";
            case AutomationStepType.Wait:
                var wait = SafeDeserialize<WaitStepConfig>(step.StepConfigJson);
                return wait is null ? "Wait" : $"Wait {wait.Amount} {wait.Unit}";
            case AutomationStepType.Condition:
                var cond = SafeDeserialize<ConditionStepConfig>(step.StepConfigJson);
                return cond is null ? "Condition" : cond.Subject + (string.IsNullOrEmpty(cond.Operand) ? "" : $" ({cond.Operand})");
            case AutomationStepType.UpdateContactField:
                return $"Update {SafeDeserialize<UpdateContactFieldStepConfig>(step.StepConfigJson)?.Field}";
            case AutomationStepType.SendWebhook:
                return $"Webhook: {SafeDeserialize<SendWebhookStepConfig>(step.StepConfigJson)?.Url}";
            default:
                return step.StepType.ToString();
        }
    }

    private static T? SafeDeserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [Authorize(Policy = "automation.create,automation.edit")]
    public async Task<IActionResult> Save(int? id, string? triggerType, string? leadFormId)
    {
        await PopulateLeadAdsFormsAsync();
        await PopulateTemplateOptionsAsync();

        if (id is null)
        {
            ViewData["Title"] = "New Automation";
            // Supports the "Set up automation" deep link from the Lead Ads forms list, which
            // jumps straight into a pre-scoped FacebookLeadReceived trigger instead of making the
            // admin remember which raw form id to paste in.
            var model = new AutomationFormViewModel();
            if (!string.IsNullOrEmpty(triggerType) && TryParseEnum<AutomationTriggerType>(triggerType, out _))
            {
                model.TriggerType = triggerType;
                model.LeadFormId = leadFormId;
            }

            return View(model);
        }

        var automation = await _db.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (automation is null)
        {
            return NotFound();
        }

        var steps = await _db.AutomationSteps.AsNoTracking().Where(s => s.AutomationId == id).ToListAsync();
        ViewData["Title"] = "Edit Automation";
        return View(AutomationFormViewModel.FromEntity(automation, steps));
    }

    [HttpPost]
    [Authorize(Policy = "automation.create,automation.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(AutomationFormViewModel model)
    {
        ViewData["Title"] = model.Id is null ? "New Automation" : "Edit Automation";
        await PopulateLeadAdsFormsAsync();
        await PopulateTemplateOptionsAsync();

        List<AutomationStepNode>? tree;
        try
        {
            tree = JsonSerializer.Deserialize<List<AutomationStepNode>>(model.StepsJson, JsonOptions);
        }
        catch (JsonException)
        {
            ModelState.AddModelError(string.Empty, "Steps data is malformed — try rebuilding the automation.");
            return View("Save", model);
        }

        if (tree is null || tree.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one step.");
            return View("Save", model);
        }

        if (!TryParseEnum<AutomationTriggerType>(model.TriggerType, out var triggerType))
        {
            ModelState.AddModelError(string.Empty, "Choose a trigger.");
            return View("Save", model);
        }

        if (!ModelState.IsValid)
        {
            return View("Save", model);
        }

        try
        {
            AutomationTreeBuilder.ValidateTree(tree, depth: 0);
            await AutomationTreeBuilder.ValidateTemplateParamsAsync(tree, _db);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Save", model);
        }

        var triggerConfigJson = AutomationTreeBuilder.BuildTriggerConfigJson(
            triggerType, model.KeywordsCsv, model.KeywordMatchType, model.KeywordCaseSensitive, model.InteractiveReplyIdsCsv, model.LeadFormId);

        Automation automation;
        if (model.Id is null)
        {
            automation = new Automation { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value, Name = model.Name.Trim(), Description = model.Description };
            _db.Automations.Add(automation);
        }
        else
        {
            var existing = await _db.Automations.FirstOrDefaultAsync(a => a.Id == model.Id);
            if (existing is null)
            {
                return NotFound();
            }

            automation = existing;
            automation.Name = model.Name.Trim();
            automation.Description = model.Description;
            automation.UpdatedAt = DateTime.UtcNow;

            var oldSteps = await _db.AutomationSteps.Where(s => s.AutomationId == automation.Id).ToListAsync();
            _db.AutomationSteps.RemoveRange(oldSteps);
        }

        automation.TriggerType = triggerType;
        automation.TriggerConfigJson = triggerConfigJson;
        automation.IsActive = model.IsActive;

        await _db.SaveChangesAsync(); // need automation.Id for step FKs

        var newSteps = new List<AutomationStep>();
        AutomationTreeBuilder.FlattenTree(tree, automation.Id, null, null, newSteps);
        _db.AutomationSteps.AddRange(newSteps);
        await _db.SaveChangesAsync();

        this.Notify(model.Id is null ? "Automation created." : "Automation updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "automation.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var automation = await _db.Automations.FirstOrDefaultAsync(a => a.Id == id);
        if (automation is null)
        {
            return NotFound();
        }

        automation.IsActive = !automation.IsActive;
        automation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        this.Notify(automation.IsActive ? "Automation activated." : "Automation paused.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Runs this automation right now against a real phone number — sends real WhatsApp messages,
    /// not a simulation — so an admin can verify a whole step tree (sends, waits, branches) works
    /// before trusting it with real leads, without waiting for a genuine trigger event. Works
    /// whether or not the automation is currently Active. Redirects to Logs so the result (and
    /// each step it actually took) is immediately visible.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "automation.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(int id, string phone, string? firstName)
    {
        var automation = await _db.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (automation is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            this.Notify("Enter a phone number to test with.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var rawPhone = phone.Trim();
        var normalized = PhoneNumberNormalizer.Normalize(rawPhone);
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Phone == rawPhone || c.Phone == normalized || c.Phone == "+" + normalized);
        if (contact is null)
        {
            var defaultStatusId = await _db.Statuses.Where(s => s.IsDefault).Select(s => (int?)s.Id).FirstOrDefaultAsync()
                ?? await _db.Statuses.Select(s => (int?)s.Id).FirstOrDefaultAsync();
            var defaultSourceId = await _db.Sources.Select(s => (int?)s.Id).FirstOrDefaultAsync();
            if (defaultStatusId is null || defaultSourceId is null)
            {
                this.Notify("Create at least one Status and Source before testing an automation.", "danger");
                return RedirectToAction(nameof(Index));
            }

            contact = new Contact
            {
                WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
                FirstName = string.IsNullOrWhiteSpace(firstName) ? "Test" : firstName.Trim(),
                LastName = string.Empty,
                Phone = normalized,
                Type = ContactType.Lead,
                StatusId = defaultStatusId.Value,
                SourceId = defaultSourceId.Value,
            };
            _db.Contacts.Add(contact);
            await _db.SaveChangesAsync();
        }

        var (status, errorMessage) = await _automationEngine.RunAutomationForTestAsync(id, contact.Id);
        this.Notify(
            status == AutomationLogStatus.Success ? $"Test run to {phone} completed — see the log below." : $"Test run to {phone} didn't finish cleanly: {errorMessage}",
            status == AutomationLogStatus.Success ? "success" : "danger");

        return RedirectToAction(nameof(Logs), new { id });
    }

    [HttpPost]
    [Authorize(Policy = "automation.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var automation = await _db.Automations.FirstOrDefaultAsync(a => a.Id == id);
        if (automation is null)
        {
            return NotFound();
        }

        _db.Automations.Remove(automation);
        await _db.SaveChangesAsync();

        this.Notify("Automation deleted.");
        return RedirectToAction(nameof(Index));
    }

    private static bool TryParseEnum<TEnum>(string value, out TEnum result) where TEnum : struct =>
        Enum.TryParse(value, ignoreCase: true, out result);

    /// <summary>Feeds the trigger canvas's "Facebook Lead Ads form" dropdown (FacebookLeadReceived
    /// scoping) — populated on every Save GET/POST render regardless of the current trigger type,
    /// same as how KeywordMatch/InteractiveReply's fields already sit unused-but-present when a
    /// different trigger is selected.</summary>
    private async Task PopulateLeadAdsFormsAsync()
    {
        ViewData["LeadAdsForms"] = await _db.LeadAdsForms
            .OrderByDescending(f => f.FormCreatedTime ?? f.CreatedAt)
            .Select(f => new { id = f.FormId, name = f.FormName })
            .ToListAsync();
    }

    /// <summary>Feeds the SendTemplate step's template picker/live-preview/variable-count logic —
    /// only approved templates, since Meta rejects a send against anything else.</summary>
    private async Task PopulateTemplateOptionsAsync()
    {
        ViewData["TemplateOptions"] = await _db.WhatsappTemplates.AsNoTracking()
            .Where(t => t.Status == TemplateStatus.Approved && t.MetaTemplateId != null)
            .OrderBy(t => t.TemplateName)
            .Select(t => new TemplateOption(
                t.MetaTemplateId!, t.TemplateName, t.HeaderFormat, t.HeaderParamsCount, t.BodyParamsCount, t.FooterParamsCount, t.BodyText, t.Language,
                t.HeaderText, t.HeaderMediaUrl, t.FooterText, t.ButtonsJson))
            .ToListAsync();
    }

}
