using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.EmailAutomations;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class EmailAutomationsController : Controller
{
    private const int PageSize = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);

    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly EmailAutomationEngine _automationEngine;

    public EmailAutomationsController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor, EmailAutomationEngine automationEngine)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
        _automationEngine = automationEngine;
    }

    [Authorize(Policy = "email_automation.view")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var query = _db.EmailAutomations.AsNoTracking().OrderByDescending(a => a.CreatedAt);
        return View(await PagedList<EmailAutomation>.CreateAsync(query, page, PageSize));
    }

    [Authorize(Policy = "email_automation.view")]
    public async Task<IActionResult> Logs(int id, int page = 1)
    {
        var automation = await _db.EmailAutomations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (automation is null)
        {
            return NotFound();
        }

        ViewData["Automation"] = automation;
        var query = _db.EmailAutomationLogs.AsNoTracking().Include(l => l.Subscriber).Where(l => l.AutomationId == id).OrderByDescending(l => l.CreatedAt);
        return View(await PagedList<EmailAutomationLog>.CreateAsync(query, page, PageSize));
    }

    [Authorize(Policy = "email_automation.create,email_automation.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        await PopulateOptionsAsync();

        if (id is null)
        {
            ViewData["Title"] = "New Email Automation";
            return View(new EmailAutomationFormViewModel());
        }

        var automation = await _db.EmailAutomations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (automation is null)
        {
            return NotFound();
        }

        var steps = await _db.EmailAutomationSteps.AsNoTracking().Where(s => s.AutomationId == id).ToListAsync();
        ViewData["Title"] = "Edit Email Automation";
        ViewData["HasSteps"] = steps.Count > 0;
        return View(EmailAutomationFormViewModel.FromEntity(automation, steps));
    }

    [HttpPost]
    [Authorize(Policy = "email_automation.create,email_automation.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmailAutomationFormViewModel model)
    {
        ViewData["Title"] = model.Id is null ? "New Email Automation" : "Edit Email Automation";
        await PopulateOptionsAsync();

        List<EmailAutomationStepNode>? tree;
        try
        {
            tree = JsonSerializer.Deserialize<List<EmailAutomationStepNode>>(model.StepsJson, JsonOptions);
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

        if (!Enum.TryParse<EmailAutomationTriggerType>(model.TriggerType, ignoreCase: true, out var triggerType))
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
            EmailAutomationTreeBuilder.ValidateTree(tree);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Save", model);
        }

        var triggerConfigJson = triggerType == EmailAutomationTriggerType.TagApplied
            ? JsonSerializer.Serialize(new TagAppliedTriggerConfig { TagId = model.TagId }, JsonOptions)
            : JsonSerializer.Serialize(new ListScopedTriggerConfig { ListId = model.ListId }, JsonOptions);

        EmailAutomation automation;
        if (model.Id is null)
        {
            automation = new EmailAutomation { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value, Name = model.Name.Trim(), Description = model.Description };
            _db.EmailAutomations.Add(automation);
        }
        else
        {
            var existing = await _db.EmailAutomations.FirstOrDefaultAsync(a => a.Id == model.Id);
            if (existing is null)
            {
                return NotFound();
            }

            automation = existing;
            automation.Name = model.Name.Trim();
            automation.Description = model.Description;
            automation.UpdatedAt = DateTime.UtcNow;

            var oldSteps = await _db.EmailAutomationSteps.Where(s => s.AutomationId == automation.Id).ToListAsync();
            _db.EmailAutomationSteps.RemoveRange(oldSteps);
        }

        automation.TriggerType = triggerType;
        automation.TriggerConfigJson = triggerConfigJson;
        automation.IsActive = model.IsActive;

        await _db.SaveChangesAsync(); // need automation.Id for step FKs

        var newSteps = new List<EmailAutomationStep>();
        EmailAutomationTreeBuilder.FlattenTree(tree, automation.Id, null, null, newSteps);
        _db.EmailAutomationSteps.AddRange(newSteps);
        await _db.SaveChangesAsync();

        this.Notify(model.Id is null ? "Email automation created." : "Email automation updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "email_automation.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var automation = await _db.EmailAutomations.FirstOrDefaultAsync(a => a.Id == id);
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

    /// <summary>Runs this automation right now against a real subscriber — sends real emails, not
    /// a simulation — mirrors AutomationsController.Test.</summary>
    [HttpPost]
    [Authorize(Policy = "email_automation.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(int id, string email)
    {
        var automation = await _db.EmailAutomations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (automation is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            this.Notify("Enter an email address to test with.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var trimmedEmail = email.Trim();
        var subscriber = await _db.Contacts.FirstOrDefaultAsync(c => c.Email == trimmedEmail);
        if (subscriber is null)
        {
            var defaultStatusId = await _db.Statuses.Where(s => s.IsDefault).Select(s => (int?)s.Id).FirstOrDefaultAsync()
                ?? await _db.Statuses.Select(s => (int?)s.Id).FirstOrDefaultAsync();
            var defaultSourceId = await _db.Sources.Select(s => (int?)s.Id).FirstOrDefaultAsync();
            if (defaultStatusId is null || defaultSourceId is null)
            {
                this.Notify("Create at least one Status and Source before testing an email automation.", "danger");
                return RedirectToAction(nameof(Index));
            }

            subscriber = new Contact
            {
                WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
                FirstName = "Test",
                LastName = string.Empty,
                Email = trimmedEmail,
                EmailStatus = EmailSubscriberStatus.Transactional,
                StatusId = defaultStatusId.Value,
                SourceId = defaultSourceId.Value,
            };
            _db.Contacts.Add(subscriber);
            await _db.SaveChangesAsync();
        }

        var (status, errorMessage) = await _automationEngine.RunAutomationForTestAsync(id, subscriber.Id);
        var testFailed = status == EmailAutomationLogStatus.Failed;
        this.Notify(
            testFailed ? $"Test run to {email} didn't finish cleanly: {errorMessage}"
            : status == EmailAutomationLogStatus.Partial ? $"Test run to {email} started — now waiting on a Wait step. See the log below."
            : $"Test run to {email} completed — see the log below.",
            testFailed ? "danger" : "success");

        return RedirectToAction(nameof(Logs), new { id });
    }

    [HttpPost]
    [Authorize(Policy = "email_automation.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var automation = await _db.EmailAutomations.FirstOrDefaultAsync(a => a.Id == id);
        if (automation is null)
        {
            return NotFound();
        }

        _db.EmailAutomations.Remove(automation);
        await _db.SaveChangesAsync();

        this.Notify("Email automation deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateOptionsAsync()
    {
        ViewData["ListOptions"] = await _db.EmailLists.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new SelectListItem { Value = l.Id.ToString(), Text = l.Name }).ToListAsync();
        ViewData["TagOptions"] = await _db.EmailTags.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Name }).ToListAsync();
    }
}
