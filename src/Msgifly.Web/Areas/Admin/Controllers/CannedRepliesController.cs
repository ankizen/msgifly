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
public class CannedRepliesController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public CannedRepliesController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "canned_reply.view")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var query = _db.CannedReplies.AsNoTracking().OrderBy(r => r.Title);
        return View(await PagedList<CannedReply>.CreateAsync(query, page, PageSize));
    }

    [Authorize(Policy = "canned_reply.create,canned_reply.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        var model = new CannedReplyFormViewModel();

        if (id is not null)
        {
            var reply = await _db.CannedReplies.FindAsync(id.Value);
            if (reply is null)
            {
                return NotFound();
            }

            model = new CannedReplyFormViewModel { Id = reply.Id, Title = reply.Title, Description = reply.Description, IsPublic = reply.IsPublic };
        }

        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "canned_reply.create,canned_reply.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CannedReplyFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (model.Id is null)
        {
            _db.CannedReplies.Add(new CannedReply { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value, Title = model.Title, Description = model.Description, IsPublic = model.IsPublic });
            this.Notify("Canned reply created.");
        }
        else
        {
            var reply = await _db.CannedReplies.FindAsync(model.Id.Value);
            if (reply is null)
            {
                return NotFound();
            }

            reply.Title = model.Title;
            reply.Description = model.Description;
            reply.IsPublic = model.IsPublic;
            reply.UpdatedAt = DateTime.UtcNow;
            this.Notify("Canned reply updated.");
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "canned_reply.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var reply = await _db.CannedReplies.FindAsync(id);
        if (reply is null)
        {
            return NotFound();
        }

        _db.CannedReplies.Remove(reply);
        await _db.SaveChangesAsync();
        this.Notify("Canned reply deleted.");
        return RedirectToAction(nameof(Index));
    }
}
