using System.Globalization;
using System.Security.Claims;
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
using Msgifly.Web.Services;
using Msgifly.Web.Services.Automations;
using Msgifly.Web.Services.WhatsApp;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class ContactsController : Controller
{
    private const int PageSize = 25;
    private readonly ApplicationDbContext _db;
    private readonly AutomationEngine _automationEngine;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public ContactsController(ApplicationDbContext db, AutomationEngine automationEngine, IWhatsAppService whatsAppService, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _automationEngine = automationEngine;
        _whatsAppService = whatsAppService;
        _workspaceAccessor = workspaceAccessor;
    }

    private static readonly int[] AllowedPageSizes = [25, 50, 100];

    [Authorize(Policy = "contact.view")]
    public async Task<IActionResult> Index(string? search, int? statusId, int? sourceId, string? leadFormId, string? pageSize, int page = 1)
    {
        // "all" bypasses paging entirely (int.MaxValue as the Take() count) rather than picking
        // an arbitrary large-but-finite cap — a personal CRM's contact list is never going to be
        // large enough for that to matter, and this way nothing is ever silently truncated.
        var effectivePageSize = pageSize == "all"
            ? int.MaxValue
            : int.TryParse(pageSize, out var parsed) && AllowedPageSizes.Contains(parsed) ? parsed : PageSize;

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

        if (!string.IsNullOrEmpty(leadFormId))
        {
            query = query.Where(c => c.LeadAdsFormId == leadFormId);
        }

        ViewData["Search"] = search;
        ViewData["StatusId"] = statusId;
        ViewData["SourceId"] = sourceId;
        ViewData["LeadFormId"] = leadFormId;
        ViewData["PageSize"] = pageSize == "all" ? "all" : effectivePageSize.ToString();
        ViewData["StatusOptions"] = await BuildOptionsAsync(_db.Statuses, s => s.Id, s => s.Name);
        ViewData["SourceOptions"] = await BuildOptionsAsync(_db.Sources, s => s.Id, s => s.Name);
        ViewData["LeadAdsFormOptions"] = await _db.LeadAdsForms.AsNoTracking()
            .OrderBy(f => f.FormName)
            .Select(f => new SelectListItem { Value = f.FormId, Text = f.FormName })
            .ToListAsync();
        ViewData["TemplateOptions"] = await _db.WhatsappTemplates.AsNoTracking()
            .Where(t => t.Status == TemplateStatus.Approved && t.MetaTemplateId != null)
            .OrderBy(t => t.TemplateName)
            .Select(t => new TemplateOption(t.MetaTemplateId!, t.TemplateName, t.HeaderFormat, t.HeaderParamsCount, t.BodyParamsCount, t.FooterParamsCount, t.BodyText))
            .ToListAsync();
        ViewData["FlowOptions"] = await _db.Flows.AsNoTracking()
            .Where(f => f.Status == FlowStatus.Published && f.MetaFlowId != null)
            .OrderBy(f => f.Name)
            .Select(f => new FlowOption(f.MetaFlowId!, f.Name))
            .ToListAsync();
        ViewData["GroupOptions"] = await _db.ContactGroups.AsNoTracking()
            .Where(g => g.Type == ContactGroupType.Static)
            .OrderBy(g => g.Name)
            .Select(g => new GroupOption(g.Id, g.Name))
            .ToListAsync();

        return View(await PagedList<Contact>.CreateAsync(query, page, effectivePageSize));
    }

    /// <summary>
    /// The "Send Template" quick action on the Contacts list — sends one template to one person
    /// right now, distinct from Campaigns (which are for bulk sends to a filtered/picked
    /// segment). Recorded as a normal outbound ChatMessage on that contact's conversation
    /// (finding-or-creating the Chat exactly like the public API's MessagesController does), so
    /// it shows up in the Chat inbox like any other message rather than living only in a
    /// separate campaign report.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTemplate(int contactId, string templateId, string? headerParam, List<string>? bodyParams)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId);
        if (contact is null)
        {
            return NotFound();
        }

        var template = await _db.WhatsappTemplates.FirstOrDefaultAsync(t => t.MetaTemplateId == templateId && t.Status == TemplateStatus.Approved);
        if (template is null)
        {
            this.Notify("Choose an approved template.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var request = new TemplateSendRequest
        {
            TemplateName = template.TemplateName,
            Language = template.Language,
            HeaderFormat = template.HeaderFormat,
            HeaderText = string.Equals(template.HeaderFormat, "TEXT", StringComparison.OrdinalIgnoreCase) ? headerParam : null,
            HeaderMediaUrl = string.Equals(template.HeaderFormat, "TEXT", StringComparison.OrdinalIgnoreCase) ? null : headerParam,
            BodyParams = (bodyParams ?? []).Take(template.BodyParamsCount).ToList(),
        };

        var result = await _whatsAppService.SendTemplateMessageAsync(contact.Phone, request);
        if (!result.Success)
        {
            this.Notify($"Couldn't send: {result.ErrorMessage}", "danger");
            return RedirectToAction(nameof(Index));
        }

        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.ReceiverId == contact.Phone);
        if (chat is null)
        {
            chat = new Chat { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value, ReceiverId = contact.Phone, Name = contact.FullName };
            _db.Chats.Add(chat);
        }

        var rendered = TemplateMessageRenderer.ForChatMessage(template, request);
        chat.Name = chat.Name == contact.Phone ? contact.FullName : chat.Name;
        chat.LastMessage = Truncate(rendered.DisplayText, 80);
        chat.LastMessageTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _db.ChatMessages.Add(new ChatMessage
        {
            ChatId = chat.Id,
            SenderId = chat.WaNoId ?? "agent",
            Message = rendered.DisplayText,
            MessageType = rendered.MediaMessageType ?? "text",
            Url = rendered.MediaUrl,
            WhatsappMessageId = result.Data,
            StaffId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null,
            Status = MessageDeliveryStatus.Sent,
            SentAt = DateTime.UtcNow,
            TemplateName = template.TemplateName,
            TimeSent = DateTime.UtcNow,
            IsRead = true,
        });
        await _db.SaveChangesAsync();

        this.Notify($"Template sent to {contact.FullName}.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// The "Send Flow" quick action on the Contacts list — mirrors SendTemplate above, but for a
    /// published WhatsApp Flow. flow_token is a fresh guid per send purely to correlate the
    /// eventual nfm_reply back to who it was sent to; the actual answers are recorded when that
    /// reply lands, not here.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendFlow(int contactId, string flowId)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId);
        if (contact is null)
        {
            return NotFound();
        }

        var flow = await _db.Flows.FirstOrDefaultAsync(f => f.MetaFlowId == flowId && f.Status == FlowStatus.Published);
        if (flow is null)
        {
            this.Notify("Choose a published flow.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var firstScreenId = FirstScreenId(flow.FlowJson);
        if (firstScreenId is null)
        {
            this.Notify("Couldn't determine the flow's first screen — check its JSON.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var flowToken = Guid.NewGuid().ToString("N");
        var result = await _whatsAppService.SendFlowMessageAsync(contact.Phone, flow.MetaFlowId!, flowToken, $"Please fill out {flow.Name}", "Start", firstScreenId);
        if (!result.Success)
        {
            this.Notify($"Couldn't send: {result.ErrorMessage}", "danger");
            return RedirectToAction(nameof(Index));
        }

        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.ReceiverId == contact.Phone);
        if (chat is null)
        {
            chat = new Chat { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value, ReceiverId = contact.Phone, Name = contact.FullName };
            _db.Chats.Add(chat);
        }

        chat.Name = chat.Name == contact.Phone ? contact.FullName : chat.Name;
        chat.LastMessage = Truncate($"Flow: {flow.Name}", 80);
        chat.LastMessageTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _db.ChatMessages.Add(new ChatMessage
        {
            ChatId = chat.Id,
            SenderId = chat.WaNoId ?? "agent",
            Message = $"Flow: {flow.Name}",
            MessageType = "text",
            WhatsappMessageId = result.Data,
            StaffId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null,
            Status = MessageDeliveryStatus.Sent,
            SentAt = DateTime.UtcNow,
            TimeSent = DateTime.UtcNow,
            IsRead = true,
        });
        await _db.SaveChangesAsync();

        this.Notify($"Flow sent to {contact.FullName}.");
        return RedirectToAction(nameof(Index));
    }

    private static string? FirstScreenId(string flowJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(flowJson);
            var screens = doc.RootElement.GetProperty("screens");
            foreach (var screen in screens.EnumerateArray())
            {
                if (screen.TryGetProperty("id", out var idProp))
                {
                    return idProp.GetString();
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }
        catch (KeyNotFoundException)
        {
        }

        return null;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length > maxLength ? text[..maxLength] + "…" : text;

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
                WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
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
                Phone = PhoneNumberNormalizer.Normalize(model.Phone),
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
            contact.Phone = PhoneNumberNormalizer.Normalize(model.Phone);
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

    /// <summary>Ids travel as a comma-separated query string (?ids=1,2,3) rather than form fields
    /// so the existing shared _ConfirmDialog component — a bare form posting to one URL, no other
    /// inputs — works for bulk delete without needing its own special case.</summary>
    [HttpPost]
    [Authorize(Policy = "contact.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDelete(string ids)
    {
        var contactIds = (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        if (contactIds.Count == 0)
        {
            this.Notify("No contacts selected.", "warning");
            return RedirectToAction(nameof(Index));
        }

        // The workspace query filter already confines this to the current tenant's own contacts —
        // an id for another workspace simply matches nothing rather than needing an explicit check.
        var contacts = await _db.Contacts.Where(c => contactIds.Contains(c.Id)).ToListAsync();
        _db.Contacts.RemoveRange(contacts);
        await _db.SaveChangesAsync();

        this.Notify($"{contacts.Count} contact(s) deleted.");
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
                WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
                FirstName = firstName,
                LastName = lastName ?? string.Empty,
                Phone = PhoneNumberNormalizer.Normalize(phone),
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
