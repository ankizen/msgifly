using System.Globalization;
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
using Msgifly.Web.Services.Automations;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class ContactsController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;
    private readonly AutomationEngine _automationEngine;

    public ContactsController(ApplicationDbContext db, AutomationEngine automationEngine)
    {
        _db = db;
        _automationEngine = automationEngine;
    }

    [Authorize(Policy = "contact.view")]
    public async Task<IActionResult> Index(string? search, int? statusId, int? sourceId, int page = 1)
    {
        var query = _db.Contacts.AsNoTracking()
            .Include(c => c.Status)
            .Include(c => c.Source)
            .Include(c => c.AssignedTo)
            .OrderByDescending(c => c.CreatedAt)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => c.FirstName.Contains(search) || c.LastName.Contains(search)
                || c.Phone.Contains(search) || (c.Email != null && c.Email.Contains(search)));
        }

        if (statusId is not null)
        {
            query = query.Where(c => c.StatusId == statusId);
        }

        if (sourceId is not null)
        {
            query = query.Where(c => c.SourceId == sourceId);
        }

        ViewData["Search"] = search;
        ViewData["StatusId"] = statusId;
        ViewData["SourceId"] = sourceId;
        ViewData["StatusOptions"] = await BuildOptionsAsync(_db.Statuses, s => s.Id, s => s.Name);
        ViewData["SourceOptions"] = await BuildOptionsAsync(_db.Sources, s => s.Id, s => s.Name);

        return View(await PagedList<Contact>.CreateAsync(query, page, PageSize));
    }

    [Authorize(Policy = "contact.create,contact.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        var model = new ContactFormViewModel();

        if (id is not null)
        {
            var contact = await _db.Contacts.Include(c => c.Notes.OrderByDescending(n => n.CreatedAt))
                .FirstOrDefaultAsync(c => c.Id == id);
            if (contact is null)
            {
                return NotFound();
            }

            model = new ContactFormViewModel
            {
                Id = contact.Id,
                FirstName = contact.FirstName,
                LastName = contact.LastName,
                Company = contact.Company,
                Type = contact.Type,
                Description = contact.Description,
                CountryCode = contact.CountryCode,
                Zip = contact.Zip,
                City = contact.City,
                State = contact.State,
                Address = contact.Address,
                AssignedToId = contact.AssignedToId,
                StatusId = contact.StatusId,
                SourceId = contact.SourceId,
                Email = contact.Email,
                Website = contact.Website,
                Phone = contact.Phone,
                IsEnabled = contact.IsEnabled,
                Notes = contact.Notes.ToList(),
            };
        }

        await PopulateOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "contact.create,contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ContactFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model);
            return View(model);
        }

        Contact? createdContact = null;
        if (model.Id is null)
        {
            var contact = new Contact
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                Company = model.Company,
                Type = model.Type,
                Description = model.Description,
                CountryCode = model.CountryCode,
                Zip = model.Zip,
                City = model.City,
                State = model.State,
                Address = model.Address,
                AssignedToId = model.AssignedToId,
                StatusId = model.StatusId,
                SourceId = model.SourceId,
                Email = model.Email,
                Website = model.Website,
                Phone = model.Phone,
                IsEnabled = model.IsEnabled,
                DateAssigned = model.AssignedToId is not null ? DateTime.UtcNow : null,
            };
            _db.Contacts.Add(contact);
            createdContact = contact;
            this.Notify("Contact created.");
        }
        else
        {
            var contact = await _db.Contacts.FindAsync(model.Id.Value);
            if (contact is null)
            {
                return NotFound();
            }

            if (contact.AssignedToId != model.AssignedToId)
            {
                contact.DateAssigned = DateTime.UtcNow;
            }

            if (contact.StatusId != model.StatusId)
            {
                contact.LastStatusChange = DateTime.UtcNow;
            }

            contact.FirstName = model.FirstName;
            contact.LastName = model.LastName;
            contact.Company = model.Company;
            contact.Type = model.Type;
            contact.Description = model.Description;
            contact.CountryCode = model.CountryCode;
            contact.Zip = model.Zip;
            contact.City = model.City;
            contact.State = model.State;
            contact.Address = model.Address;
            contact.AssignedToId = model.AssignedToId;
            contact.StatusId = model.StatusId;
            contact.SourceId = model.SourceId;
            contact.Email = model.Email;
            contact.Website = model.Website;
            contact.Phone = model.Phone;
            contact.IsEnabled = model.IsEnabled;
            contact.UpdatedAt = DateTime.UtcNow;
            this.Notify("Contact updated.");
        }

        await _db.SaveChangesAsync();

        if (createdContact is not null)
        {
            await _automationEngine.RunForTriggerAsync(AutomationTriggerType.NewContactCreated, createdContact.Id, new AutomationContext());
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(int contactId, string description)
    {
        if (!string.IsNullOrWhiteSpace(description))
        {
            _db.ContactNotes.Add(new ContactNote { ContactId = contactId, Description = description.Trim() });
            await _db.SaveChangesAsync();
            this.Notify("Note added.");
        }

        return RedirectToAction(nameof(Save), new { id = contactId });
    }

    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNote(int noteId)
    {
        var note = await _db.ContactNotes.FindAsync(noteId);
        if (note is null)
        {
            return NotFound();
        }

        var contactId = note.ContactId;
        _db.ContactNotes.Remove(note);
        await _db.SaveChangesAsync();
        this.Notify("Note deleted.");
        return RedirectToAction(nameof(Save), new { id = contactId });
    }

    [HttpPost]
    [Authorize(Policy = "contact.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var contact = await _db.Contacts.FindAsync(id);
        if (contact is null)
        {
            return NotFound();
        }

        _db.Contacts.Remove(contact);
        await _db.SaveChangesAsync();
        this.Notify("Contact deleted.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = "contact.bulk_import")]
    public IActionResult Import() => View();

    [HttpPost]
    [Authorize(Policy = "contact.bulk_import")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile? file, ContactType type)
    {
        if (file is null || file.Length == 0)
        {
            this.Notify("Please choose a CSV file to upload.", "danger");
            return RedirectToAction(nameof(Import));
        }

        var defaultStatusId = await _db.Statuses.Where(s => s.IsDefault).Select(s => (int?)s.Id).FirstOrDefaultAsync()
            ?? await _db.Statuses.Select(s => (int?)s.Id).FirstOrDefaultAsync();
        var defaultSourceId = await _db.Sources.Select(s => (int?)s.Id).FirstOrDefaultAsync();

        if (defaultStatusId is null || defaultSourceId is null)
        {
            this.Notify("Create at least one Status and Source before importing contacts.", "danger");
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

        var newContacts = new List<Contact>();
        var rowNumber = 1;

        while (await csv.ReadAsync())
        {
            rowNumber++;
            var firstName = csv.GetField("firstname")?.Trim();
            var lastName = csv.GetField("lastname")?.Trim();
            var phone = csv.GetField("phone")?.Trim();
            var email = csv.GetField("email")?.Trim();
            var company = csv.GetField("company")?.Trim();

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(phone))
            {
                result.Skipped++;
                result.Errors.Add($"Row {rowNumber}: first name and phone are required.");
                continue;
            }

            newContacts.Add(new Contact
            {
                FirstName = firstName,
                LastName = lastName ?? string.Empty,
                Phone = phone,
                Email = string.IsNullOrWhiteSpace(email) ? null : email,
                Company = string.IsNullOrWhiteSpace(company) ? null : company,
                Type = type,
                StatusId = defaultStatusId.Value,
                SourceId = defaultSourceId.Value,
            });
            result.Imported++;
        }

        if (newContacts.Count > 0)
        {
            _db.Contacts.AddRange(newContacts);
            await _db.SaveChangesAsync();
        }

        return View("ImportResult", result);
    }

    private async Task PopulateOptionsAsync(ContactFormViewModel model)
    {
        model.StatusOptions = await BuildOptionsAsync(_db.Statuses, s => s.Id, s => s.Name);
        model.SourceOptions = await BuildOptionsAsync(_db.Sources, s => s.Id, s => s.Name);
        model.AssigneeOptions = await _db.Users.AsNoTracking().OrderBy(u => u.FirstName)
            .Select(u => new SelectListItem { Value = u.Id.ToString(), Text = u.FirstName + " " + u.LastName })
            .ToListAsync();
    }

    private static async Task<List<SelectListItem>> BuildOptionsAsync<T>(
        IQueryable<T> source, Func<T, int> idSelector, Func<T, string> textSelector)
        where T : class
    {
        var items = await source.AsNoTracking().ToListAsync();
        return items
            .Select(x => new SelectListItem { Value = idSelector(x).ToString(), Text = textSelector(x) })
            .OrderBy(x => x.Text)
            .ToList();
    }
}
