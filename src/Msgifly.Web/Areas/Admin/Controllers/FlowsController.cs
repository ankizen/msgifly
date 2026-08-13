using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.WhatsApp;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class FlowsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;

    public FlowsController(ApplicationDbContext db, IWhatsAppService whatsAppService)
    {
        _db = db;
        _whatsAppService = whatsAppService;
    }

    [Authorize(Policy = "flow.view")]
    public async Task<IActionResult> Index()
    {
        var flows = await _db.Flows.AsNoTracking().OrderByDescending(f => f.UpdatedAt).ToListAsync();
        return View(flows);
    }

    [HttpPost]
    [Authorize(Policy = "flow.view")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync()
    {
        var result = await _whatsAppService.SyncFlowsAsync();
        this.Notify(
            result.Success ? $"Synced {result.Data!.Count} flow(s) from Meta." : $"Sync failed: {result.ErrorMessage}",
            result.Success ? "success" : "danger");

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = "flow.create")]
    public IActionResult Create()
    {
        ViewData["Title"] = "New Flow";
        return View("Save", new FlowFormViewModel());
    }

    [HttpPost]
    [Authorize(Policy = "flow.create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FlowFormViewModel model)
    {
        ViewData["Title"] = "New Flow";
        if (!ModelState.IsValid)
        {
            return View("Save", model);
        }

        if (!IsValidJson(model.FlowJson))
        {
            ModelState.AddModelError(nameof(model.FlowJson), "That isn't valid JSON.");
            return View("Save", model);
        }

        var result = await _whatsAppService.CreateFlowAsync(model.Name.Trim(), [model.Category], model.FlowJson);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage!);
            return View("Save", model);
        }

        _db.Flows.Add(new Flow
        {
            MetaFlowId = result.Data,
            Name = model.Name.Trim(),
            CategoriesJson = JsonSerializer.Serialize(new[] { model.Category }),
            FlowJson = model.FlowJson,
            Status = FlowStatus.Draft,
        });
        await _db.SaveChangesAsync();

        this.Notify("Flow created as a draft on Meta.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = "flow.edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var flow = await _db.Flows.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
        if (flow is null)
        {
            return NotFound();
        }

        if (flow.Status != FlowStatus.Draft)
        {
            this.Notify($"Flows in status {flow.Status} can't be edited — Meta locks the layout once published.", "danger");
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = "Edit Flow";
        return View("Save", FlowFormViewModel.FromEntity(flow));
    }

    [HttpPost]
    [Authorize(Policy = "flow.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FlowFormViewModel model)
    {
        ViewData["Title"] = "Edit Flow";
        model.Id = id;
        if (!ModelState.IsValid)
        {
            return View("Save", model);
        }

        if (!IsValidJson(model.FlowJson))
        {
            ModelState.AddModelError(nameof(model.FlowJson), "That isn't valid JSON.");
            return View("Save", model);
        }

        var existing = await _db.Flows.FirstOrDefaultAsync(f => f.Id == id);
        if (existing is null)
        {
            return NotFound();
        }

        if (existing.Status != FlowStatus.Draft)
        {
            this.Notify($"Flows in status {existing.Status} can't be edited.", "danger");
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrEmpty(existing.MetaFlowId))
        {
            this.Notify("This flow was never submitted to Meta — nothing to update yet.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var result = await _whatsAppService.UpdateFlowJsonAsync(existing.MetaFlowId, model.FlowJson);
        if (!result.Success)
        {
            existing.SubmissionError = result.ErrorMessage;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            ModelState.AddModelError(string.Empty, result.ErrorMessage!);
            return View("Save", model);
        }

        existing.Name = model.Name.Trim();
        existing.CategoriesJson = JsonSerializer.Serialize(new[] { model.Category });
        existing.FlowJson = model.FlowJson;
        existing.SubmissionError = null;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        this.Notify("Flow layout updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "flow.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id)
    {
        var flow = await _db.Flows.FirstOrDefaultAsync(f => f.Id == id);
        if (flow is null)
        {
            return NotFound();
        }

        if (string.IsNullOrEmpty(flow.MetaFlowId))
        {
            this.Notify("This flow was never submitted to Meta — nothing to publish yet.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var result = await _whatsAppService.PublishFlowAsync(flow.MetaFlowId);
        if (!result.Success)
        {
            this.Notify($"Couldn't publish: {result.ErrorMessage}", "danger");
            return RedirectToAction(nameof(Index));
        }

        flow.Status = FlowStatus.Published;
        flow.SubmissionError = null;
        flow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        this.Notify("Flow published — it can now be sent to customers.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "flow.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var flow = await _db.Flows.FirstOrDefaultAsync(f => f.Id == id);
        if (flow is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrEmpty(flow.MetaFlowId))
        {
            var result = await _whatsAppService.DeleteFlowAsync(flow.MetaFlowId);
            if (!result.Success)
            {
                this.Notify($"Couldn't delete: {result.ErrorMessage}", "danger");
                return RedirectToAction(nameof(Index));
            }
        }

        _db.Flows.Remove(flow);
        await _db.SaveChangesAsync();

        this.Notify("Flow deleted.");
        return RedirectToAction(nameof(Index));
    }

    private static bool IsValidJson(string text)
    {
        try
        {
            JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
