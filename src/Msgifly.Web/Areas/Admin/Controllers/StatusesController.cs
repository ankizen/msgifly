using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.ViewModels;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class StatusesController : Controller
{
    private const int PageSize = 15;
    private readonly ApplicationDbContext _db;

    public StatusesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [Authorize(Policy = "status.view")]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var query = _db.Statuses.AsNoTracking().OrderBy(s => s.Name).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Name.Contains(search));
        }

        ViewData["Search"] = search;
        return View(await PagedList<Status>.CreateAsync(query, page, PageSize));
    }

    [Authorize(Policy = "status.create,status.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        if (id is null)
        {
            return View(new StatusFormViewModel());
        }

        var status = await _db.Statuses.FindAsync(id.Value);
        if (status is null)
        {
            return NotFound();
        }

        return View(new StatusFormViewModel { Id = status.Id, Name = status.Name, Color = status.Color, IsDefault = status.IsDefault });
    }

    [HttpPost]
    [Authorize(Policy = "status.create,status.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(StatusFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // The original enforced "only one default status" with a plain update-then-create and no
        // locking (master doc §10 item 15 — a real race condition). Do it properly in a transaction.
        // EF Core's retrying execution strategy (Program.cs's EnableRetryOnFailure) can't wrap a
        // manually-opened transaction directly — it needs to own the whole retry unit itself.
        var notFound = false;
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();

            if (model.IsDefault)
            {
                await _db.Statuses.Where(s => s.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false));
            }

            if (model.Id is null)
            {
                _db.Statuses.Add(new Status { Name = model.Name, Color = model.Color, IsDefault = model.IsDefault });
            }
            else
            {
                var status = await _db.Statuses.FindAsync(model.Id.Value);
                if (status is null)
                {
                    notFound = true;
                    return;
                }

                status.Name = model.Name;
                status.Color = model.Color;
                status.IsDefault = model.IsDefault;
                status.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        if (notFound)
        {
            return NotFound();
        }

        this.Notify(model.Id is null ? "Status created." : "Status updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "status.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var status = await _db.Statuses.FindAsync(id);
        if (status is null)
        {
            return NotFound();
        }

        if (status.IsDefault)
        {
            this.Notify("Can't delete the default status — set another status as default first.", "danger");
            return RedirectToAction(nameof(Index));
        }

        if (await _db.Contacts.AnyAsync(c => c.StatusId == id))
        {
            this.Notify("Can't delete a status that's still assigned to contacts.", "danger");
            return RedirectToAction(nameof(Index));
        }

        _db.Statuses.Remove(status);
        await _db.SaveChangesAsync();
        this.Notify("Status deleted.");
        return RedirectToAction(nameof(Index));
    }
}
