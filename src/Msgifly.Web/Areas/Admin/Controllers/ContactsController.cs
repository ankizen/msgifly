using System.Globalization;
using System.Security.Claims;
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
using Msgifly.Web.Services;
using Msgifly.Web.Services.Automations;
using Msgifly.Web.Services.EmailAutomations;
using Msgifly.Web.Services.EmailSequences;
using Msgifly.Web.Services.Groups;
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
    private readonly EmailAutomationEngine _emailAutomationEngine;
    private readonly EmailSequenceService _emailSequenceService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly ContactGroupResolver _groupResolver;

    public ContactsController(
        ApplicationDbContext db,
        AutomationEngine automationEngine,
        EmailAutomationEngine emailAutomationEngine,
        EmailSequenceService emailSequenceService,
        IWhatsAppService whatsAppService,
        ICurrentWorkspaceAccessor workspaceAccessor,
        ContactGroupResolver groupResolver)
    {
        _db = db;
        _automationEngine = automationEngine;
        _emailAutomationEngine = emailAutomationEngine;
        _emailSequenceService = emailSequenceService;
        _whatsAppService = whatsAppService;
        _workspaceAccessor = workspaceAccessor;
        _groupResolver = groupResolver;
    }

    private static readonly int[] AllowedPageSizes = [25, 50, 100];

    [Authorize(Policy = "contact.view")]
    public async Task<IActionResult> Index(string? search, int? statusId, int? sourceId, string? leadFormId, int? groupId, string? pageSize, int page = 1)
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

        if (groupId is not null)
        {
            var group = await _db.ContactGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId);
            var memberIds = group is null ? new List<int>() : await _groupResolver.ResolveContactIdsAsync(group);
            query = query.Where(c => memberIds.Contains(c.Id));
        }

        ViewData["Search"] = search;
        ViewData["StatusId"] = statusId;
        ViewData["SourceId"] = sourceId;
        ViewData["LeadFormId"] = leadFormId;
        ViewData["GroupId"] = groupId;
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
        ViewData["AutomationOptions"] = await _db.Automations.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new AutomationOption(a.Id, a.Name))
            .ToListAsync();
        ViewData["GroupOptions"] = await _db.ContactGroups.AsNoTracking()
            .Where(g => g.Type == ContactGroupType.Static)
            .OrderBy(g => g.Name)
            .Select(g => new GroupOption(g.Id, g.Name))
            .ToListAsync();
        // Separate from GroupOptions above: the "Add to group" modal can only target a Static
        // group (you can't manually add a member to a Dynamic one), but filtering the list BY
        // group membership is a read, so Dynamic groups belong here too.
        ViewData["GroupFilterOptions"] = await _db.ContactGroups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupOption(g.Id, g.Name, g.Type == ContactGroupType.Dynamic ? "Dynamic" : "Static"))
            .ToListAsync();
        ViewData["EmailAutomationOptions"] = await _db.EmailAutomations.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.Name)
            .Select(a => new SelectListItem { Value = a.Id.ToString(), Text = a.Name }).ToListAsync();
        ViewData["EmailSequenceOptions"] = await _db.EmailSequences.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync();

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

        var (success, error) = await SendTemplateToContactAsync(contact, template, request);
        if (!success)
        {
            this.Notify($"Couldn't send: {error}", "danger");
            return RedirectToAction(nameof(Index));
        }

        this.Notify($"Template sent to {contact.FullName}.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// The "Send Template" bulk action on the Contacts list — same send as the single-contact
    /// action above, just looped over a hand-picked selection instead of one row. Deliberately
    /// sequential and synchronous (no background job): this is for "blast this to the dozen people
    /// I just selected right now," not mass sending — that's what Campaigns is already for.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkSendTemplate(string ids, string templateId, string? headerParam, List<string>? bodyParams)
    {
        var contactIds = ParseIds(ids);
        if (contactIds.Count == 0)
        {
            this.Notify("No contacts selected.", "warning");
            return RedirectToAction(nameof(Index));
        }

        var template = await _db.WhatsappTemplates.FirstOrDefaultAsync(t => t.MetaTemplateId == templateId && t.Status == TemplateStatus.Approved);
        if (template is null)
        {
            this.Notify("Choose an approved template.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var contacts = await _db.Contacts.Where(c => contactIds.Contains(c.Id)).ToListAsync();
        var sentCount = 0;
        var failures = new List<string>();

        foreach (var contact in contacts)
        {
            var request = new TemplateSendRequest
            {
                TemplateName = template.TemplateName,
                Language = template.Language,
                HeaderFormat = template.HeaderFormat,
                HeaderText = string.Equals(template.HeaderFormat, "TEXT", StringComparison.OrdinalIgnoreCase) ? headerParam : null,
                HeaderMediaUrl = string.Equals(template.HeaderFormat, "TEXT", StringComparison.OrdinalIgnoreCase) ? null : headerParam,
                BodyParams = (bodyParams ?? []).Take(template.BodyParamsCount).ToList(),
            };

            var (success, error) = await SendTemplateToContactAsync(contact, template, request);
            if (success)
            {
                sentCount++;
            }
            else
            {
                failures.Add($"{contact.FullName} ({error})");
            }
        }

        var message = failures.Count == 0
            ? $"Template sent to all {sentCount} contact(s)."
            : $"Sent to {sentCount} of {contacts.Count}. Failed: {string.Join("; ", failures.Take(5))}{(failures.Count > 5 ? $" and {failures.Count - 5} more" : "")}.";
        this.Notify(message, failures.Count == 0 ? "success" : "warning");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Sends an already-resolved template to one contact and records it as an outbound
    /// ChatMessage, finding-or-creating the Chat exactly like the public API's MessagesController
    /// does — shared by the single-contact and bulk Send Template actions above so there's one
    /// place that does the actual send-and-record, not two copies drifting apart.</summary>
    private async Task<(bool Success, string? Error)> SendTemplateToContactAsync(Contact contact, WhatsappTemplate template, TemplateSendRequest request)
    {
        var result = await _whatsAppService.SendTemplateMessageAsync(contact.Phone, request);
        if (!result.Success)
        {
            return (false, result.ErrorMessage);
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

        return (true, null);
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

    /// <summary>
    /// The "Run Automation" quick action on the Contacts list — runs one automation's full step
    /// tree right now for one contact, via the same engine path as the Automations page's own
    /// Test button and MCP's retry_automation_for_contact. Doesn't require the automation to be
    /// Active and doesn't inflate its ExecutionCount/LastExecutedAt — this is a manual one-off
    /// injection (e.g. a lead that came in before the automation existed), not a real trigger fire.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAutomation(int contactId, int automationId)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId);
        if (contact is null)
        {
            return NotFound();
        }

        var (status, errorMessage) = await _automationEngine.RunAutomationForTestAsync(automationId, contactId, "Manual");
        // Partial isn't a failure — it just means the run reached a Wait step and is scheduled to
        // resume later (e.g. this automation's first step already sent a real template). Only
        // Failed should read as an error; treating Partial as one made a genuinely successful send
        // show up as "Couldn't run automation" with no reason, since errorMessage is null there.
        var failed = status == AutomationLogStatus.Failed;
        this.Notify(
            failed ? $"Couldn't run automation for {contact.FullName}: {errorMessage}"
            : status == AutomationLogStatus.Partial ? $"Automation started for {contact.FullName} — now waiting on a Wait step."
            : $"Automation completed for {contact.FullName}.",
            failed ? "danger" : "success");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Bulk version of RunAutomation — same sequential-loop pattern as BulkSendTemplate.</summary>
    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkRunAutomation(string ids, int automationId)
    {
        var contactIds = ParseIds(ids);
        if (contactIds.Count == 0)
        {
            this.Notify("No contacts selected.", "warning");
            return RedirectToAction(nameof(Index));
        }

        var contacts = await _db.Contacts.Where(c => contactIds.Contains(c.Id)).ToListAsync();
        var succeeded = 0;
        var failures = new List<string>();

        foreach (var contact in contacts)
        {
            var (status, errorMessage) = await _automationEngine.RunAutomationForTestAsync(automationId, contact.Id, "Manual");
            if (status != AutomationLogStatus.Failed)
            {
                succeeded++;
            }
            else
            {
                failures.Add($"{contact.FullName} ({errorMessage})");
            }
        }

        var message = failures.Count == 0
            ? $"Automation started for all {succeeded} contact(s)."
            : $"Started for {succeeded} of {contacts.Count}. Failed: {string.Join("; ", failures.Take(5))}{(failures.Count > 5 ? $" and {failures.Count - 5} more" : "")}.";
        this.Notify(message, failures.Count == 0 ? "success" : "warning");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>The "Run Email Automation" quick action — same idea as RunAutomation above, but
    /// through the independent EmailAutomationEngine (Contact IS the email subscriber, so this
    /// takes the same contactId, no separate lookup needed).</summary>
    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunEmailAutomation(int contactId, int automationId)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId);
        if (contact is null)
        {
            return NotFound();
        }

        var (status, errorMessage) = await _emailAutomationEngine.RunAutomationForTestAsync(automationId, contactId);
        var failed = status == EmailAutomationLogStatus.Failed;
        this.Notify(
            failed ? $"Couldn't run email automation for {contact.FullName}: {errorMessage}"
            : status == EmailAutomationLogStatus.Partial ? $"Email automation started for {contact.FullName} — now waiting on a Wait step."
            : $"Email automation completed for {contact.FullName}.",
            failed ? "danger" : "success");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Bulk version of RunEmailAutomation — same sequential-loop pattern as BulkRunAutomation.</summary>
    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkRunEmailAutomation(string ids, int automationId)
    {
        var contactIds = ParseIds(ids);
        if (contactIds.Count == 0)
        {
            this.Notify("No contacts selected.", "warning");
            return RedirectToAction(nameof(Index));
        }

        var contacts = await _db.Contacts.Where(c => contactIds.Contains(c.Id)).ToListAsync();
        var succeeded = 0;
        var failures = new List<string>();

        foreach (var contact in contacts)
        {
            var (status, errorMessage) = await _emailAutomationEngine.RunAutomationForTestAsync(automationId, contact.Id);
            if (status != EmailAutomationLogStatus.Failed)
            {
                succeeded++;
            }
            else
            {
                failures.Add($"{contact.FullName} ({errorMessage})");
            }
        }

        var message = failures.Count == 0
            ? $"Email automation started for all {succeeded} contact(s)."
            : $"Started for {succeeded} of {contacts.Count}. Failed: {string.Join("; ", failures.Take(5))}{(failures.Count > 5 ? $" and {failures.Count - 5} more" : "")}.";
        this.Notify(message, failures.Count == 0 ? "success" : "warning");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToEmailSequence(int contactId, int sequenceId)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId);
        if (contact is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(contact.Email))
        {
            this.Notify($"{contact.FullName} has no email address — add one first.", "danger");
            return RedirectToAction(nameof(Index));
        }

        await _emailSequenceService.SubscribeAsync(sequenceId, contactId);
        this.Notify($"{contact.FullName} added to the email sequence.");
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

    /// <summary>Ids travel as a comma-separated query string (?ids=1,2,3) rather than form fields
    /// so the existing shared _ConfirmDialog component — a bare form posting to one URL, no other
    /// inputs — works for bulk delete without needing its own special case. Reused by every bulk
    /// action that takes a hand-picked contact-id selection.</summary>
    private static List<int> ParseIds(string ids) =>
        (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

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
                EmailStatus = contact.EmailStatus,
                SelectedListIds = await _db.EmailSubscriberLists.AsNoTracking().Where(l => l.SubscriberId == id).Select(l => l.ListId).ToListAsync(),
                SelectedTagIds = await _db.EmailSubscriberTags.AsNoTracking().Where(t => t.SubscriberId == id).Select(t => t.TagId).ToListAsync(),
                EmailCustomFieldValues = SafeDeserializeCustomFields(contact.EmailCustomFieldsJson),
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
                EmailStatus = model.EmailStatus,
                EmailCustomFieldsJson = JsonSerializer.Serialize(model.EmailCustomFieldValues),
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
            contact.EmailStatus = model.EmailStatus;
            contact.EmailCustomFieldsJson = JsonSerializer.Serialize(model.EmailCustomFieldValues);
            contact.UpdatedAt = DateTime.UtcNow;
            this.Notify("Contact updated.");
        }

        await _db.SaveChangesAsync();

        var contactId = createdContact?.Id ?? model.Id!.Value;
        await SyncEmailListsAndTagsAsync(contactId, model.SelectedListIds, model.SelectedTagIds);

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

        await ClearStaleChatNameAsync(contact);
        _db.Contacts.Remove(contact);
        await _db.SaveChangesAsync();
        this.Notify("Contact deleted.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Chat.Name only ever reflects a real, verified WhatsApp profile name when it came
    /// from an inbound webhook (WhatsAppWebhookController sets it from contacts[].profile.name).
    /// A quick-send/automation instead seeds a brand-new Chat's Name from the CRM Contact's own
    /// FullName at send time (AutomationEngine.ResolveOrCreateChatAsync) — never confirmed against
    /// WhatsApp itself. Deleting that Contact leaves that CRM-sourced name behind with no record
    /// backing it, looking like a verified name it never was. Reset it to the phone number — this
    /// codebase's existing "no name known yet" sentinel (see SendTemplate/SendFlow, which detect
    /// "not yet named" via `chat.Name == contact.Phone`) — but only if the name still exactly
    /// matches what this contact was called; if it differs, a real inbound reply already refreshed
    /// it with a genuine WhatsApp profile name, which must not be touched.</summary>
    private async Task ClearStaleChatNameAsync(Contact contact)
    {
        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.ReceiverId == contact.Phone);
        if (chat is not null && chat.Name == contact.FullName)
        {
            chat.Name = chat.ReceiverId;
        }
    }

    [HttpPost]
    [Authorize(Policy = "contact.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDelete(string ids)
    {
        var contactIds = ParseIds(ids);

        if (contactIds.Count == 0)
        {
            this.Notify("No contacts selected.", "warning");
            return RedirectToAction(nameof(Index));
        }

        // The workspace query filter already confines this to the current tenant's own contacts —
        // an id for another workspace simply matches nothing rather than needing an explicit check.
        var contacts = await _db.Contacts.Where(c => contactIds.Contains(c.Id)).ToListAsync();
        foreach (var contact in contacts)
        {
            await ClearStaleChatNameAsync(contact);
        }

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
        ViewData["EmailListOptions"] = await _db.EmailLists.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new SelectListItem { Value = l.Id.ToString(), Text = l.Name }).ToListAsync();
        ViewData["EmailTagOptions"] = await _db.EmailTags.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Name }).ToListAsync();
        ViewData["EmailCustomFields"] = await _db.EmailCustomFields.AsNoTracking().OrderBy(f => f.Label).ToListAsync();
    }

    /// <summary>Diffs the posted list/tag selections against what's currently stored and applies
    /// only the delta — mirrors the same add/remove-difference pattern used everywhere else in
    /// this app for many-to-many form fields.</summary>
    private async Task SyncEmailListsAndTagsAsync(int contactId, List<int> listIds, List<int> tagIds)
    {
        var existingLists = await _db.EmailSubscriberLists.Where(l => l.SubscriberId == contactId).ToListAsync();
        _db.EmailSubscriberLists.RemoveRange(existingLists.Where(l => !listIds.Contains(l.ListId)));
        foreach (var listId in listIds.Except(existingLists.Select(l => l.ListId)))
        {
            _db.EmailSubscriberLists.Add(new EmailSubscriberList { SubscriberId = contactId, ListId = listId });
        }

        var existingTags = await _db.EmailSubscriberTags.Where(t => t.SubscriberId == contactId).ToListAsync();
        _db.EmailSubscriberTags.RemoveRange(existingTags.Where(t => !tagIds.Contains(t.TagId)));
        foreach (var tagId in tagIds.Except(existingTags.Select(t => t.TagId)))
        {
            _db.EmailSubscriberTags.Add(new EmailSubscriberTag { SubscriberId = contactId, TagId = tagId });
        }

        await _db.SaveChangesAsync();
    }

    private static Dictionary<string, string> SafeDeserializeCustomFields(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
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
