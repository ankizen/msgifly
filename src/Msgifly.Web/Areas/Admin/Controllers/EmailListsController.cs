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
public class EmailListsController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public EmailListsController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "email_list.view")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var query = _db.EmailLists.AsNoTracking().OrderBy(l => l.Name);
        var paged = await PagedList<EmailList>.CreateAsync(query, page, PageSize);

        var listIds = paged.Items.Select(l => l.Id).ToList();
        var counts = await _db.EmailSubscriberLists.AsNoTracking()
            .Where(m => listIds.Contains(m.ListId))
            .GroupBy(m => m.ListId)
            .Select(g => new { ListId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ListId, x => x.Count);
        ViewData["MemberCounts"] = counts;

        return View(paged);
    }

    [Authorize(Policy = "email_list.create,email_list.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        if (id is null)
        {
            return View(new EmailListFormViewModel());
        }

        var list = await _db.EmailLists.FindAsync(id.Value);
        if (list is null)
        {
            return NotFound();
        }

        return View(new EmailListFormViewModel { Id = list.Id, Name = list.Name, Description = list.Description });
    }

    [HttpPost]
    [Authorize(Policy = "email_list.create,email_list.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmailListFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (model.Id is null)
        {
            _db.EmailLists.Add(new EmailList { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value, Name = model.Name, Description = model.Description });
            this.Notify("List created.");
        }
        else
        {
            var list = await _db.EmailLists.FindAsync(model.Id.Value);
            if (list is null)
            {
                return NotFound();
            }

            list.Name = model.Name;
            list.Description = model.Description;
            list.UpdatedAt = DateTime.UtcNow;
            this.Notify("List updated.");
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "email_list.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var list = await _db.EmailLists.FindAsync(id);
        if (list is null)
        {
            return NotFound();
        }

        _db.EmailLists.Remove(list);
        await _db.SaveChangesAsync();
        this.Notify("List deleted.");
        return RedirectToAction(nameof(Index));
    }
}
