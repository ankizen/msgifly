using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Services.WhatsApp;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class TemplatesController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;

    public TemplatesController(ApplicationDbContext db, IWhatsAppService whatsAppService)
    {
        _db = db;
        _whatsAppService = whatsAppService;
    }

    [Authorize(Policy = "template.view")]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var query = _db.WhatsappTemplates.AsNoTracking().OrderBy(t => t.TemplateName).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(t => t.TemplateName.Contains(search));
        }

        ViewData["Search"] = search;
        return View(await PagedList<WhatsappTemplate>.CreateAsync(query, page, PageSize));
    }

    [HttpPost]
    [Authorize(Policy = "template.load_template")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync()
    {
        var result = await _whatsAppService.SyncTemplatesAsync();
        this.Notify(
            result.Success ? $"Synced {result.Data} template(s) from Meta." : $"Sync failed: {result.ErrorMessage}",
            result.Success ? "success" : "danger");

        return RedirectToAction(nameof(Index));
    }
}
