using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
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
using Msgifly.Web.Services.EmailSequences;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class EmailSubscribersController : Controller
{
    private const int PageSize = 25;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly EmailAutomationEngine _automationEngine;
    private readonly EmailSequenceService _sequenceService;

    public EmailSubscribersController(
        ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor, EmailAutomationEngine automationEngine, EmailSequenceService sequenceService)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
        _automationEngine = automationEngine;
        _sequenceService = sequenceService;
    }

    [Authorize(Policy = "email_subscriber.view")]
    public async Task<IActionResult> Index(string? search, EmailSubscriberStatus? status, int? listId, int? tagId, int page = 1)
    {
        var query = _db.EmailSubscribers.AsNoTracking().OrderByDescending(s => s.CreatedAt).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Email.Contains(search) || (s.FirstName != null && s.FirstName.Contains(search)) || (s.LastName != null && s.LastName.Contains(search)));
        }

        if (status is not null)
        {
            query = query.Where(s => s.Status == status);
        }

        if (listId is not null)
        {
            query = query.Where(s => _db.EmailSubscriberLists.Any(l => l.SubscriberId == s.Id && l.ListId == listId));
        }

        if (tagId is not null)
        {
            query = query.Where(s => _db.EmailSubscriberTags.Any(t => t.SubscriberId == s.Id && t.TagId == tagId));
        }

        ViewData["Search"] = search;
        ViewData["StatusFilter"] = status;
        ViewData["ListFilter"] = listId;
        ViewData["TagFilter"] = tagId;
        ViewData["ListOptions"] = await _db.EmailLists.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new SelectListItem { Value = l.Id.ToString(), Text = l.Name }).ToListAsync();
        ViewData["TagOptions"] = await _db.EmailTags.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Name }).ToListAsync();
        ViewData["AutomationOptions"] = await _db.EmailAutomations.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.Name)
            .Select(a => new SelectListItem { Value = a.Id.ToString(), Text = a.Name }).ToListAsync();
        ViewData["SequenceOptions"] = await _db.EmailSequences.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync();

        return View(await PagedList<EmailSubscriber>.CreateAsync(query, page, PageSize));
    }

    [Authorize(Policy = "email_subscriber.create,email_subscriber.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        await PopulateOptionsAsync();

        if (id is null)
        {
            return View(new EmailSubscriberFormViewModel());
        }

        var subscriber = await _db.EmailSubscribers.FindAsync(id.Value);
        if (subscriber is null)
        {
            return NotFound();
        }

        var model = new EmailSubscriberFormViewModel
        {
            Id = subscriber.Id,
            Email = subscriber.Email,
            FirstName = subscriber.FirstName,
            LastName = subscriber.LastName,
            Phone = subscriber.Phone,
            Type = subscriber.Type,
            Status = subscriber.Status,
            SourceId = subscriber.SourceId,
            SelectedListIds = await _db.EmailSubscriberLists.AsNoTracking().Where(l => l.SubscriberId == id).Select(l => l.ListId).ToListAsync(),
            SelectedTagIds = await _db.EmailSubscriberTags.AsNoTracking().Where(t => t.SubscriberId == id).Select(t => t.TagId).ToListAsync(),
            CustomFieldValues = SafeDeserialize<Dictionary<string, string>>(subscriber.CustomFieldsJson) ?? [],
        };

        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "email_subscriber.create,email_subscriber.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmailSubscriberFormViewModel model)
    {
        var emailTaken = await _db.EmailSubscribers.AnyAsync(s => s.Email == model.Email && s.Id != (model.Id ?? 0));
        if (emailTaken)
        {
            ModelState.AddModelError(nameof(model.Email), "A subscriber with this email already exists.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync();
            return View(model);
        }

        var customFieldsJson = JsonSerializer.Serialize(model.CustomFieldValues);

        EmailSubscriber subscriber;
        bool isNew = model.Id is null;
        if (isNew)
        {
            subscriber = new EmailSubscriber { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value };
            _db.EmailSubscribers.Add(subscriber);
        }
        else
        {
            var existing = await _db.EmailSubscribers.FindAsync(model.Id!.Value);
            if (existing is null)
            {
                return NotFound();
            }

            subscriber = existing;
            subscriber.UpdatedAt = DateTime.UtcNow;
        }

        subscriber.Email = model.Email.Trim();
        subscriber.FirstName = model.FirstName;
        subscriber.LastName = model.LastName;
        subscriber.Phone = model.Phone;
        subscriber.Type = model.Type;
        subscriber.Status = model.Status;
        subscriber.SourceId = model.SourceId;
        subscriber.CustomFieldsJson = customFieldsJson;

        await _db.SaveChangesAsync();

        await SyncListsAndTagsAsync(subscriber.Id, model.SelectedListIds, model.SelectedTagIds);

        this.Notify(isNew ? "Subscriber created." : "Subscriber updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "email_subscriber.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var subscriber = await _db.EmailSubscribers.FindAsync(id);
        if (subscriber is null)
        {
            return NotFound();
        }

        _db.EmailSubscribers.Remove(subscriber);
        await _db.SaveChangesAsync();
        this.Notify("Subscriber deleted.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = "email_subscriber.bulk_import")]
    public IActionResult Import() => View();

    [HttpPost]
    [Authorize(Policy = "email_subscriber.bulk_import")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            this.Notify("Please choose a CSV file to upload.", "danger");
            return RedirectToAction(nameof(Import));
        }

        var result = new ImportContactsResultViewModel();
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", ""),
        };

        using var reader = new StreamReader(file.OpenReadStream());
        using var csv = new CsvReader(reader, csvConfig);

        await csv.ReadAsync();
        csv.ReadHeader();

        var existingEmails = await _db.EmailSubscribers.AsNoTracking().Select(s => s.Email).ToHashSetAsync(StringComparer.OrdinalIgnoreCase);
        var newSubscribers = new List<EmailSubscriber>();
        var rowNumber = 1;

        while (await csv.ReadAsync())
        {
            rowNumber++;
            var email = csv.GetField("email")?.Trim();
            var firstName = csv.GetField("firstname")?.Trim();
            var lastName = csv.GetField("lastname")?.Trim();

            if (string.IsNullOrWhiteSpace(email))
            {
                result.Skipped++;
                result.Errors.Add($"Row {rowNumber}: email is required.");
                continue;
            }

            if (!existingEmails.Add(email))
            {
                result.Skipped++;
                result.Errors.Add($"Row {rowNumber}: {email} already exists.");
                continue;
            }

            newSubscribers.Add(new EmailSubscriber
            {
                WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
            });
            result.Imported++;
        }

        if (newSubscribers.Count > 0)
        {
            _db.EmailSubscribers.AddRange(newSubscribers);
            await _db.SaveChangesAsync();
        }

        return View("ImportResult", result);
    }

    /// <summary>Injects one subscriber into an automation's step tree right now, via the same
    /// engine path as a real SubscriberAdded/TagApplied/ListApplied trigger — mirrors
    /// ContactsController.RunAutomation.</summary>
    [HttpPost]
    [Authorize(Policy = "email_subscriber.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAutomation(int subscriberId, int automationId)
    {
        var subscriber = await _db.EmailSubscribers.FirstOrDefaultAsync(s => s.Id == subscriberId);
        if (subscriber is null)
        {
            return NotFound();
        }

        var (status, errorMessage) = await _automationEngine.RunAutomationForTestAsync(automationId, subscriberId);
        var failed = status == EmailAutomationLogStatus.Failed;
        this.Notify(
            failed ? $"Couldn't run automation for {subscriber.Email}: {errorMessage}"
            : status == EmailAutomationLogStatus.Partial ? $"Automation started for {subscriber.Email} — now waiting on a Wait step."
            : $"Automation completed for {subscriber.Email}.",
            failed ? "danger" : "success");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "email_subscriber.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToSequence(int subscriberId, int sequenceId)
    {
        var subscriber = await _db.EmailSubscribers.FirstOrDefaultAsync(s => s.Id == subscriberId);
        if (subscriber is null)
        {
            return NotFound();
        }

        await _sequenceService.SubscribeAsync(sequenceId, subscriberId);
        this.Notify($"{subscriber.Email} added to the sequence.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = "email_subscriber.edit")]
    public async Task<IActionResult> CustomFields()
    {
        var fields = await _db.EmailCustomFields.AsNoTracking().OrderBy(f => f.Label).ToListAsync();
        return View(fields);
    }

    [HttpPost]
    [Authorize(Policy = "email_subscriber.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCustomField(EmailCustomFieldFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Key) || string.IsNullOrWhiteSpace(model.Label))
        {
            this.Notify("Key and label are required.", "danger");
            return RedirectToAction(nameof(CustomFields));
        }

        var slug = model.Key.Trim().ToLowerInvariant().Replace(' ', '_');
        var optionsJson = model.FieldType == EmailCustomFieldType.Dropdown && !string.IsNullOrWhiteSpace(model.OptionsCsv)
            ? JsonSerializer.Serialize(model.OptionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            : null;

        if (model.Id is null)
        {
            if (await _db.EmailCustomFields.AnyAsync(f => f.Key == slug))
            {
                this.Notify("A custom field with this key already exists.", "danger");
                return RedirectToAction(nameof(CustomFields));
            }

            _db.EmailCustomFields.Add(new EmailCustomField
            {
                WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
                Key = slug,
                Label = model.Label.Trim(),
                FieldType = model.FieldType,
                OptionsJson = optionsJson,
            });
            this.Notify("Custom field created.");
        }
        else
        {
            var field = await _db.EmailCustomFields.FindAsync(model.Id.Value);
            if (field is null)
            {
                return NotFound();
            }

            field.Label = model.Label.Trim();
            field.FieldType = model.FieldType;
            field.OptionsJson = optionsJson;
            this.Notify("Custom field updated.");
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(CustomFields));
    }

    [HttpPost]
    [Authorize(Policy = "email_subscriber.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCustomField(int id)
    {
        var field = await _db.EmailCustomFields.FindAsync(id);
        if (field is null)
        {
            return NotFound();
        }

        _db.EmailCustomFields.Remove(field);
        await _db.SaveChangesAsync();
        this.Notify("Custom field deleted.");
        return RedirectToAction(nameof(CustomFields));
    }

    private async Task SyncListsAndTagsAsync(int subscriberId, List<int> listIds, List<int> tagIds)
    {
        var existingLists = await _db.EmailSubscriberLists.Where(l => l.SubscriberId == subscriberId).ToListAsync();
        _db.EmailSubscriberLists.RemoveRange(existingLists.Where(l => !listIds.Contains(l.ListId)));
        foreach (var listId in listIds.Except(existingLists.Select(l => l.ListId)))
        {
            _db.EmailSubscriberLists.Add(new EmailSubscriberList { SubscriberId = subscriberId, ListId = listId });
        }

        var existingTags = await _db.EmailSubscriberTags.Where(t => t.SubscriberId == subscriberId).ToListAsync();
        _db.EmailSubscriberTags.RemoveRange(existingTags.Where(t => !tagIds.Contains(t.TagId)));
        foreach (var tagId in tagIds.Except(existingTags.Select(t => t.TagId)))
        {
            _db.EmailSubscriberTags.Add(new EmailSubscriberTag { SubscriberId = subscriberId, TagId = tagId });
        }

        await _db.SaveChangesAsync();
    }

    private async Task PopulateOptionsAsync()
    {
        ViewData["ListOptions"] = await _db.EmailLists.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new SelectListItem { Value = l.Id.ToString(), Text = l.Name }).ToListAsync();
        ViewData["TagOptions"] = await _db.EmailTags.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Name }).ToListAsync();
        ViewData["SourceOptions"] = await _db.Sources.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync();
        ViewData["CustomFields"] = await _db.EmailCustomFields.AsNoTracking().OrderBy(f => f.Label).ToListAsync();
    }

    private static T? SafeDeserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
