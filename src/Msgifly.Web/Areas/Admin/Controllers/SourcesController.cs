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
public class SourcesController : Controller
{
    private const int PageSize = 15;
    private readonly ApplicationDbContext _db;

    public SourcesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [Authorize(Policy = "source.view")]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var query = _db.Sources.AsNoTracking().OrderBy(s => s.Name).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Name.Contains(search));
        }

        ViewData["Search"] = search;
        return View(await PagedList<Source>.CreateAsync(query, page, PageSize));
    }

    [Authorize(Policy = "source.create,source.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        if (id is null)
        {
            return View(new SourceFormViewModel());
        }

        var source = await _db.Sources.FindAsync(id.Value);
        if (source is null)
        {
            return NotFound();
        }

        return View(new SourceFormViewModel { Id = source.Id, Name = source.Name });
    }

    [HttpPost]
    [Authorize(Policy = "source.create,source.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SourceFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (model.Id is null)
        {
            _db.Sources.Add(new Source { Name = model.Name });
            this.Notify("Source created.");
        }
        else
        {
            var source = await _db.Sources.FindAsync(model.Id.Value);
            if (source is null)
            {
                return NotFound();
            }

            source.Name = model.Name;
            source.UpdatedAt = DateTime.UtcNow;
            this.Notify("Source updated.");
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "source.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var source = await _db.Sources.FindAsync(id);
        if (source is null)
        {
            return NotFound();
        }

        if (await _db.Contacts.AnyAsync(c => c.SourceId == id))
        {
            this.Notify("Can't delete a source that's still assigned to contacts.", "danger");
            return RedirectToAction(nameof(Index));
        }

        _db.Sources.Remove(source);
        await _db.SaveChangesAsync();
        this.Notify("Source deleted.");
        return RedirectToAction(nameof(Index));
    }
}
