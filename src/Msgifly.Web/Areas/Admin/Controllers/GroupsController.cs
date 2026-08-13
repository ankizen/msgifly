using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services;
using Msgifly.Web.Services.Groups;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class GroupsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ContactGroupResolver _resolver;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public GroupsController(ApplicationDbContext db, ContactGroupResolver resolver, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _resolver = resolver;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "group.view")]
    public async Task<IActionResult> Index()
    {
        var groups = await _db.ContactGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();
        var items = new List<GroupListItem>();
        foreach (var group in groups)
        {
            items.Add(new GroupListItem
            {
                Id = group.Id,
                Name = group.Name,
                Type = group.Type,
                MemberCount = await _resolver.CountAsync(group),
                UpdatedAt = group.UpdatedAt,
            });
        }

        return View(items);
    }

    [HttpGet]
    [Authorize(Policy = "group.create")]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "New Group";
        var model = new GroupFormViewModel();
        await PopulateOptionsAsync(model);
        return View("Save", model);
    }

    [HttpPost]
    [Authorize(Policy = "group.create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GroupFormViewModel model)
    {
        ViewData["Title"] = "New Group";
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model);
            return View("Save", model);
        }

        var group = new ContactGroup
        {
            WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
            Name = model.Name.Trim(),
            Type = model.Type,
        };

        if (model.Type == ContactGroupType.Dynamic)
        {
            group.FilterJson = JsonSerializer.Serialize(new DynamicGroupFilter
            {
                RelType = model.FilterRelType,
                StatusIds = model.FilterStatusIds,
                SourceIds = model.FilterSourceIds,
            });
        }

        _db.ContactGroups.Add(group);
        await _db.SaveChangesAsync();

        if (group.Type == ContactGroupType.Static)
        {
            this.Notify("Group created — now add contacts to it.");
            return RedirectToAction(nameof(Members), new { id = group.Id });
        }

        this.Notify("Group created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = "group.edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var group = await _db.ContactGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (group is null)
        {
            return NotFound();
        }

        ViewData["Title"] = "Edit Group";
        var filter = ContactGroupResolver.ParseFilter(group.FilterJson);
        var model = new GroupFormViewModel
        {
            Id = group.Id,
            Name = group.Name,
            Type = group.Type,
            FilterRelType = filter.RelType,
            FilterStatusIds = filter.StatusIds,
            FilterSourceIds = filter.SourceIds,
        };
        await PopulateOptionsAsync(model);
        return View("Save", model);
    }

    [HttpPost]
    [Authorize(Policy = "group.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, GroupFormViewModel model)
    {
        ViewData["Title"] = "Edit Group";
        model.Id = id;
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model);
            return View("Save", model);
        }

        var group = await _db.ContactGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group is null)
        {
            return NotFound();
        }

        group.Name = model.Name.Trim();
        if (group.Type == ContactGroupType.Dynamic)
        {
            group.FilterJson = JsonSerializer.Serialize(new DynamicGroupFilter
            {
                RelType = model.FilterRelType,
                StatusIds = model.FilterStatusIds,
                SourceIds = model.FilterSourceIds,
            });
        }

        group.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        this.Notify("Group updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "group.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var group = await _db.ContactGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group is null)
        {
            return NotFound();
        }

        _db.ContactGroups.Remove(group);
        await _db.SaveChangesAsync();
        this.Notify("Group deleted.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = "group.edit")]
    public async Task<IActionResult> Members(int id)
    {
        var group = await _db.ContactGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (group is null)
        {
            return NotFound();
        }

        if (group.Type != ContactGroupType.Static)
        {
            this.Notify("Dynamic groups re-evaluate their filter automatically — nothing to manage here.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var memberIds = await _db.ContactGroupMembers.AsNoTracking()
            .Where(m => m.GroupId == id)
            .Select(m => m.ContactId)
            .ToListAsync();

        var members = await _db.Contacts.AsNoTracking()
            .Where(c => memberIds.Contains(c.Id))
            .OrderBy(c => c.FirstName)
            .Select(c => new ContactOption(c.Id, c.FirstName + " " + c.LastName + " - " + c.Phone))
            .ToListAsync();

        var contactOptions = await _db.Contacts.AsNoTracking()
            .Where(c => !memberIds.Contains(c.Id))
            .OrderBy(c => c.FirstName)
            .Select(c => new ContactOption(c.Id, c.FirstName + " " + c.LastName + " (" + c.Type + ") - " + c.Phone))
            .ToListAsync();

        return View(new GroupMembersViewModel
        {
            GroupId = group.Id,
            GroupName = group.Name,
            Members = members,
            ContactOptions = contactOptions,
        });
    }

    [HttpPost]
    [Authorize(Policy = "group.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMembers(int id, List<int> contactIds)
    {
        var group = await _db.ContactGroups.FirstOrDefaultAsync(g => g.Id == id && g.Type == ContactGroupType.Static);
        if (group is null)
        {
            return NotFound();
        }

        await AddMemberIdsAsync(group, contactIds);
        this.Notify($"{contactIds.Count} contact(s) added.");
        return RedirectToAction(nameof(Members), new { id });
    }

    [HttpPost]
    [Authorize(Policy = "group.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveMember(int id, int contactId)
    {
        var member = await _db.ContactGroupMembers.FirstOrDefaultAsync(m => m.GroupId == id && m.ContactId == contactId);
        if (member is not null)
        {
            _db.ContactGroupMembers.Remove(member);
            await _db.SaveChangesAsync();
        }

        this.Notify("Removed from group.");
        return RedirectToAction(nameof(Members), new { id });
    }

    /// <summary>
    /// Matches each row's phone number against existing Contacts (normalized) and adds the
    /// matches as members — doesn't create new Contacts for unmatched rows, since a marketing
    /// list of raw numbers with no name/status/source shouldn't silently seed the CRM; those are
    /// reported back so the admin can decide (e.g. import them properly via Contacts/Import first).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "group.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadCsv(int id, IFormFile? file)
    {
        var group = await _db.ContactGroups.FirstOrDefaultAsync(g => g.Id == id && g.Type == ContactGroupType.Static);
        if (group is null)
        {
            return NotFound();
        }

        if (file is null || file.Length == 0)
        {
            this.Notify("Choose a CSV file to upload.", "danger");
            return RedirectToAction(nameof(Members), new { id });
        }

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

        var existingMemberIds = (await _db.ContactGroupMembers.AsNoTracking().Where(m => m.GroupId == id).Select(m => m.ContactId).ToListAsync()).ToHashSet();

        var result = new GroupCsvUploadResult { GroupId = id, GroupName = group.Name };
        var toAdd = new List<int>();

        while (await csv.ReadAsync())
        {
            var rawPhone = csv.GetField("phone")?.Trim();
            if (string.IsNullOrWhiteSpace(rawPhone))
            {
                continue;
            }

            var normalized = PhoneNumberNormalizer.Normalize(rawPhone);
            var contact = await _db.Contacts.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Phone == rawPhone || c.Phone == normalized || c.Phone == "+" + normalized);

            if (contact is null)
            {
                result.Unmatched.Add(rawPhone);
                continue;
            }

            if (existingMemberIds.Contains(contact.Id))
            {
                result.AlreadyMember++;
                continue;
            }

            toAdd.Add(contact.Id);
            existingMemberIds.Add(contact.Id);
            result.Matched++;
        }

        if (toAdd.Count > 0)
        {
            _db.ContactGroupMembers.AddRange(toAdd.Select(cid => new ContactGroupMember { GroupId = id, ContactId = cid }));
            await _db.SaveChangesAsync();
        }

        return View("UploadCsvResult", result);
    }

    /// <summary>
    /// The "Add to group" bulk action from the Contacts list multi-select — either drops the
    /// picked contacts into an existing Static group or creates a new one on the fly.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "group.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMembersBulk(string ids, int? groupId, string? newGroupName)
    {
        var contactIds = (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();

        if (contactIds.Count == 0)
        {
            this.Notify("No contacts selected.", "danger");
            return RedirectToAction("Index", "Contacts");
        }

        ContactGroup? group = null;
        if (groupId is not null)
        {
            group = await _db.ContactGroups.FirstOrDefaultAsync(g => g.Id == groupId && g.Type == ContactGroupType.Static);
        }
        else if (!string.IsNullOrWhiteSpace(newGroupName))
        {
            group = new ContactGroup { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value, Name = newGroupName.Trim(), Type = ContactGroupType.Static };
            _db.ContactGroups.Add(group);
            await _db.SaveChangesAsync();
        }

        if (group is null)
        {
            this.Notify("Choose a group or name a new one.", "danger");
            return RedirectToAction("Index", "Contacts");
        }

        var added = await AddMemberIdsAsync(group, contactIds);
        this.Notify($"{added} contact(s) added to \"{group.Name}\".");
        return RedirectToAction("Index", "Contacts");
    }

    private async Task<int> AddMemberIdsAsync(ContactGroup group, List<int> contactIds)
    {
        var existing = (await _db.ContactGroupMembers.AsNoTracking().Where(m => m.GroupId == group.Id).Select(m => m.ContactId).ToListAsync()).ToHashSet();
        var toAdd = contactIds.Distinct().Where(cid => !existing.Contains(cid)).ToList();
        if (toAdd.Count == 0)
        {
            return 0;
        }

        _db.ContactGroupMembers.AddRange(toAdd.Select(cid => new ContactGroupMember { GroupId = group.Id, ContactId = cid }));
        await _db.SaveChangesAsync();
        return toAdd.Count;
    }

    private async Task PopulateOptionsAsync(GroupFormViewModel model)
    {
        model.StatusOptions = await _db.Statuses.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync();
        model.SourceOptions = await _db.Sources.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync();
    }
}
