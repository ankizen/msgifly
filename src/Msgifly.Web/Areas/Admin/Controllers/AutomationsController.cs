using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.Automations;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class AutomationsController : Controller
{
    private const int PageSize = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);

    private readonly ApplicationDbContext _db;

    public AutomationsController(ApplicationDbContext db)
    {
        _db = db;
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

    [Authorize(Policy = "automation.create,automation.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        if (id is null)
        {
            ViewData["Title"] = "New Automation";
            return View(new AutomationFormViewModel());
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
            ValidateTree(tree, depth: 0);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Save", model);
        }

        var triggerConfigJson = BuildTriggerConfigJson(triggerType, model);

        Automation automation;
        if (model.Id is null)
        {
            automation = new Automation { Name = model.Name.Trim(), Description = model.Description };
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
        FlattenTree(tree, automation.Id, null, null, newSteps);
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

    private static string BuildTriggerConfigJson(AutomationTriggerType triggerType, AutomationFormViewModel model)
    {
        switch (triggerType)
        {
            case AutomationTriggerType.KeywordMatch:
                var keywords = (model.KeywordsCsv ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                return JsonSerializer.Serialize(new KeywordMatchTriggerConfig
                {
                    Keywords = keywords,
                    MatchType = model.KeywordMatchType,
                    CaseSensitive = model.KeywordCaseSensitive,
                }, JsonOptions);

            case AutomationTriggerType.InteractiveReply:
                var replyIds = (model.InteractiveReplyIdsCsv ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                return JsonSerializer.Serialize(new InteractiveReplyTriggerConfig { ReplyIds = replyIds }, JsonOptions);

            default:
                return "{}";
        }
    }

    /// <summary>One level of Condition nesting only — a Yes/No branch's own children must not themselves be Conditions.</summary>
    private static void ValidateTree(List<AutomationStepNode> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            if (!Enum.TryParse<AutomationStepType>(node.Type, ignoreCase: true, out var stepType))
            {
                throw new ArgumentException($"Unknown step type: {node.Type}");
            }

            if (stepType == AutomationStepType.Condition)
            {
                if (depth > 0)
                {
                    throw new ArgumentException("Conditions can't be nested inside another condition's branch.");
                }

                if (node.Yes is not null) ValidateTree(node.Yes, depth + 1);
                if (node.No is not null) ValidateTree(node.No, depth + 1);
            }
        }
    }

    private static void FlattenTree(List<AutomationStepNode> nodes, int automationId, int? parentStepId, string? branch, List<AutomationStep> output)
    {
        var position = 0;
        foreach (var node in nodes)
        {
            Enum.TryParse<AutomationStepType>(node.Type, ignoreCase: true, out var stepType);

            var step = new AutomationStep
            {
                AutomationId = automationId,
                ParentStepId = parentStepId,
                Branch = branch,
                StepType = stepType,
                StepConfigJson = node.Config.ValueKind == JsonValueKind.Undefined ? "{}" : node.Config.GetRawText(),
                Position = position++,
            };
            output.Add(step);

            if (stepType == AutomationStepType.Condition)
            {
                // Children reference this step by Id, which EF only assigns after SaveChanges —
                // AddRange + SaveChanges in the caller persists parents and children in one
                // batch, but the self-referencing FK needs the parent's Id known first. EF Core's
                // change tracker resolves this automatically via navigation fixup as long as
                // ParentStep is set instead of ParentStepId directly for not-yet-saved parents.
                if (node.Yes is not null)
                {
                    var yesChildren = new List<AutomationStep>();
                    FlattenTree(node.Yes, automationId, null, "Yes", yesChildren);
                    foreach (var child in yesChildren)
                    {
                        child.ParentStep = step;
                    }

                    output.AddRange(yesChildren);
                }

                if (node.No is not null)
                {
                    var noChildren = new List<AutomationStep>();
                    FlattenTree(node.No, automationId, null, "No", noChildren);
                    foreach (var child in noChildren)
                    {
                        child.ParentStep = step;
                    }

                    output.AddRange(noChildren);
                }
            }
        }
    }
}
