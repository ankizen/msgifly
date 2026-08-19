using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class EmailTagsController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public EmailTagsController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "email_tag.view")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var query = _db.EmailTags.AsNoTracking().OrderBy(t => t.Name);
        var paged = await PagedList<EmailTag>.CreateAsync(query, page, PageSize);

        var tagIds = paged.Items.Select(t => t.Id).ToList();
        var counts = await _db.EmailSubscriberTags.AsNoTracking()
            .Where(m => tagIds.Contains(m.TagId))
            .GroupBy(m => m.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TagId, x => x.Count);
        ViewData["MemberCounts"] = counts;

        return View(paged);
    }

    [Authorize(Policy = "email_tag.create,email_tag.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        if (id is null)
        {
            return View(new EmailTagFormViewModel());
        }

        var tag = await _db.EmailTags.FindAsync(id.Value);
        if (tag is null)
        {
            return NotFound();
        }

        return View(new EmailTagFormViewModel { Id = tag.Id, Name = tag.Name });
    }

    [HttpPost]
    [Authorize(Policy = "email_tag.create,email_tag.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmailTagFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (model.Id is null)
        {
            _db.EmailTags.Add(new EmailTag { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value, Name = model.Name });
            this.Notify("Tag created.");
        }
        else
        {
            var tag = await _db.EmailTags.FindAsync(model.Id.Value);
            if (tag is null)
            {
                return NotFound();
            }

            tag.Name = model.Name;
            tag.UpdatedAt = DateTime.UtcNow;
            this.Notify("Tag updated.");
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "email_tag.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var tag = await _db.EmailTags.FindAsync(id);
        if (tag is null)
        {
            return NotFound();
        }

        _db.EmailTags.Remove(tag);
        await _db.SaveChangesAsync();
        this.Notify("Tag deleted.");
        return RedirectToAction(nameof(Index));
    }
}
