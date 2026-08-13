using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.WhatsApp;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class TemplatesController : Controller
{
    private const int PageSize = 20;
    private const long MaxUploadBytes = 16 * 1024 * 1024; // matches Meta's own header-media cap
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IWebHostEnvironment _environment;

    public TemplatesController(ApplicationDbContext db, IWhatsAppService whatsAppService, IWebHostEnvironment environment)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _environment = environment;
    }

    /// <summary>
    /// Backs the "Choose file" button on the template editor's header-media field — Meta's own
    /// create/edit API just wants a publicly reachable URL for the sample media, so this stores
    /// the upload under wwwroot and hands back that URL rather than requiring the admin to host
    /// the file somewhere themselves first.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "template.load_template")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadHeaderMedia(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("Choose a file to upload.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return BadRequest("File is larger than WhatsApp's 16 MB limit.");
        }

        var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "templates");
        Directory.CreateDirectory(uploadsDir);
        var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var absolutePath = Path.Combine(uploadsDir, storedFileName);

        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        var publicUrl = $"{Request.Scheme}://{Request.Host}/uploads/templates/{storedFileName}";
        return Json(new { url = publicUrl });
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

    /// <summary>
    /// Sent/delivered/read/failed/clicked, combined across every way this template can go out —
    /// Campaign sends (CampaignDetail, joined on Campaign.TemplateId) and everything else
    /// (ChatMessage.TemplateName: single quick-sends, bot replies, automations, the public API).
    /// Counts key off the existing Status/DeliveryStatus enums (works for data from before this
    /// feature shipped too), not the newer per-stage timestamp columns, which are for the funnel
    /// visualization the counts alone can't show — when a message actually reached each stage.
    /// </summary>
    [Authorize(Policy = "template.view")]
    public async Task<IActionResult> Report(int id)
    {
        var template = await _db.WhatsappTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (template is null)
        {
            return NotFound();
        }

        var campaignCounts = await _db.CampaignDetails.AsNoTracking()
            .Where(d => d.Campaign.TemplateId == template.MetaTemplateId)
            .GroupBy(d => 1)
            .Select(g => new
            {
                Sent = g.Count(d => d.Status == CampaignDetailStatus.Sent),
                Delivered = g.Count(d => d.DeliveryStatus == MessageDeliveryStatus.Delivered || d.DeliveryStatus == MessageDeliveryStatus.Read),
                Read = g.Count(d => d.DeliveryStatus == MessageDeliveryStatus.Read),
                Failed = g.Count(d => d.Status == CampaignDetailStatus.Failed || d.DeliveryStatus == MessageDeliveryStatus.Failed),
                Clicked = g.Count(d => d.Clicked),
            })
            .FirstOrDefaultAsync();

        var chatCounts = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.TemplateName == template.TemplateName)
            .GroupBy(m => 1)
            .Select(g => new
            {
                Sent = g.Count(),
                Delivered = g.Count(m => m.Status == MessageDeliveryStatus.Delivered || m.Status == MessageDeliveryStatus.Read),
                Read = g.Count(m => m.Status == MessageDeliveryStatus.Read),
                Failed = g.Count(m => m.Status == MessageDeliveryStatus.Failed),
                Clicked = g.Count(m => m.Clicked),
            })
            .FirstOrDefaultAsync();

        var campaignFailures = await _db.CampaignDetails.AsNoTracking()
            .Where(d => d.Campaign.TemplateId == template.MetaTemplateId
                && (d.Status == CampaignDetailStatus.Failed || d.DeliveryStatus == MessageDeliveryStatus.Failed)
                && d.ResponseMessage != null)
            .GroupBy(d => d.ResponseMessage)
            .Select(g => new { Reason = g.Key!, Count = g.Count() })
            .ToListAsync();

        var chatFailures = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.TemplateName == template.TemplateName && m.Status == MessageDeliveryStatus.Failed && m.StatusDetail != null)
            .GroupBy(m => m.StatusDetail)
            .Select(g => new { Reason = g.Key!, Count = g.Count() })
            .ToListAsync();

        var model = new TemplateReportViewModel
        {
            TemplateId = template.Id,
            TemplateName = template.TemplateName,
            SentCount = (campaignCounts?.Sent ?? 0) + (chatCounts?.Sent ?? 0),
            DeliveredCount = (campaignCounts?.Delivered ?? 0) + (chatCounts?.Delivered ?? 0),
            ReadCount = (campaignCounts?.Read ?? 0) + (chatCounts?.Read ?? 0),
            FailedCount = (campaignCounts?.Failed ?? 0) + (chatCounts?.Failed ?? 0),
            ClickedCount = (campaignCounts?.Clicked ?? 0) + (chatCounts?.Clicked ?? 0),
            FailureReasons = [.. campaignFailures.Concat(chatFailures)
                .GroupBy(f => f.Reason)
                .Select(g => new TemplateFailureReason(g.Key, g.Sum(x => x.Count)))
                .OrderByDescending(f => f.Count)],
        };

        return View(model);
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

    [HttpGet]
    [Authorize(Policy = "template.load_template")]
    public IActionResult Create()
    {
        ViewData["Title"] = "New Template";
        return View("Save", new TemplateFormViewModel());
    }

    [HttpPost]
    [Authorize(Policy = "template.load_template")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TemplateFormViewModel model)
    {
        ViewData["Title"] = "New Template";
        if (!ModelState.IsValid)
        {
            return View("Save", model);
        }

        try
        {
            var request = model.ToRequest();
            var result = await _whatsAppService.CreateTemplateAsync(request);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage!);
                return View("Save", model);
            }

            this.Notify("Template submitted to Meta for approval.");
            return RedirectToAction(nameof(Index));
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Save", model);
        }
    }

    [HttpGet]
    [Authorize(Policy = "template.load_template")]
    public async Task<IActionResult> Edit(int id)
    {
        var template = await _db.WhatsappTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (template is null)
        {
            return NotFound();
        }

        if (string.IsNullOrEmpty(template.MetaTemplateId))
        {
            this.Notify("This template was never submitted to Meta — nothing to edit yet.", "danger");
            return RedirectToAction(nameof(Index));
        }

        if (template.Status is not (TemplateStatus.Approved or TemplateStatus.Rejected or TemplateStatus.Paused))
        {
            this.Notify($"Templates in status {template.Status} can't be edited. Allowed: Approved, Rejected, Paused.", "danger");
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = "Edit Template";
        return View("Save", TemplateFormViewModel.FromEntity(template));
    }

    [HttpPost]
    [Authorize(Policy = "template.load_template")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TemplateFormViewModel model)
    {
        ViewData["Title"] = "Edit Template";
        model.Id = id;
        if (!ModelState.IsValid)
        {
            return View("Save", model);
        }

        try
        {
            var request = model.ToRequest();
            var result = await _whatsAppService.EditTemplateAsync(id, request);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage!);
                return View("Save", model);
            }

            this.Notify("Template re-submitted to Meta for approval.");
            return RedirectToAction(nameof(Index));
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Save", model);
        }
    }

    [HttpPost]
    [Authorize(Policy = "template.load_template")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _whatsAppService.DeleteTemplateAsync(id);
        this.Notify(
            result.Success ? "Template deleted." : $"Couldn't delete: {result.ErrorMessage}",
            result.Success ? "success" : "danger");

        return RedirectToAction(nameof(Index));
    }
}
