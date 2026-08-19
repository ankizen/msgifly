using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

/// <summary>Definitions only for Contact.EmailCustomFieldsJson — reached from the Contacts page
/// toolbar, kept as its own small controller since managing field schema is a distinct action
/// from managing Contacts themselves.</summary>
[Area("Admin")]
[Authorize]
public class EmailCustomFieldsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public EmailCustomFieldsController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "contact.edit")]
    public async Task<IActionResult> Index()
    {
        var fields = await _db.EmailCustomFields.AsNoTracking().OrderBy(f => f.Label).ToListAsync();
        return View(fields);
    }

    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmailCustomFieldFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Key) || string.IsNullOrWhiteSpace(model.Label))
        {
            this.Notify("Key and label are required.", "danger");
            return RedirectToAction(nameof(Index));
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
                return RedirectToAction(nameof(Index));
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
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "contact.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var field = await _db.EmailCustomFields.FindAsync(id);
        if (field is null)
        {
            return NotFound();
        }

        _db.EmailCustomFields.Remove(field);
        await _db.SaveChangesAsync();
        this.Notify("Custom field deleted.");
        return RedirectToAction(nameof(Index));
    }
}
