using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.Campaigns;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class TemplateBotsController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public TemplateBotsController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "template_bot.view")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var query = _db.TemplateBots.AsNoTracking().OrderByDescending(b => b.CreatedAt);
        var paged = await PagedList<TemplateBot>.CreateAsync(query, page, PageSize);

        ViewData["TemplateNames"] = await _db.WhatsappTemplates.AsNoTracking()
            .Where(t => t.MetaTemplateId != null)
            .ToDictionaryAsync(t => t.MetaTemplateId!, t => t.TemplateName);

        return View(paged);
    }

    [Authorize(Policy = "template_bot.create,template_bot.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        var model = new TemplateBotFormViewModel();

        if (id is not null)
        {
            var bot = await _db.TemplateBots.FindAsync(id.Value);
            if (bot is null)
            {
                return NotFound();
            }

            model = new TemplateBotFormViewModel
            {
                Id = bot.Id,
                Name = bot.Name,
                RelType = bot.RelType,
                TemplateId = bot.TemplateId ?? string.Empty,
                ReplyType = bot.ReplyType,
                TriggersInput = string.Join(", ", ParseTriggers(bot.TriggersJson)),
                HeaderMediaUrl = bot.FileName,
                IsActive = bot.IsActive,
            };

            FillSlots(model.HeaderParams, bot.HeaderParamsJson);
            FillSlots(model.BodyParams, bot.BodyParamsJson);
        }

        await PopulateOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "template_bot.create,template_bot.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(TemplateBotFormViewModel model)
    {
        if (model.ReplyType != ReplyType.FirstMessage && model.ReplyType != ReplyType.CatchAll
            && string.IsNullOrWhiteSpace(model.TriggersInput))
        {
            ModelState.AddModelError(nameof(model.TriggersInput), "Enter at least one trigger keyword for this mode.");
        }

        var template = await _db.WhatsappTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MetaTemplateId == model.TemplateId && t.Status == TemplateStatus.Approved);
        if (template is null)
        {
            ModelState.AddModelError(nameof(model.TemplateId), "Choose an approved template.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model);
            return View(model);
        }

        var headerParamCount = string.Equals(template!.HeaderFormat, "TEXT", StringComparison.OrdinalIgnoreCase) ? template.HeaderParamsCount : 0;
        var triggersJson = JsonSerializer.Serialize(SplitTriggers(model.TriggersInput));

        if (model.Id is null)
        {
            _db.TemplateBots.Add(new TemplateBot
            {
                WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
                Name = model.Name,
                RelType = model.RelType,
                TemplateId = model.TemplateId,
                ReplyType = model.ReplyType,
                TriggersJson = triggersJson,
                FileName = model.HeaderMediaUrl,
                HeaderParamsJson = SerializeSlots(model.HeaderParams, headerParamCount),
                BodyParamsJson = SerializeSlots(model.BodyParams, template.BodyParamsCount),
                IsActive = model.IsActive,
            });
            this.Notify("Template bot created.");
        }
        else
        {
            var bot = await _db.TemplateBots.FindAsync(model.Id.Value);
            if (bot is null)
            {
                return NotFound();
            }

            bot.Name = model.Name;
            bot.RelType = model.RelType;
            bot.TemplateId = model.TemplateId;
            bot.ReplyType = model.ReplyType;
            bot.TriggersJson = triggersJson;
            bot.FileName = model.HeaderMediaUrl;
            bot.HeaderParamsJson = SerializeSlots(model.HeaderParams, headerParamCount);
            bot.BodyParamsJson = SerializeSlots(model.BodyParams, template.BodyParamsCount);
            bot.IsActive = model.IsActive;
            bot.UpdatedAt = DateTime.UtcNow;
            this.Notify("Template bot updated.");
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "template_bot.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var bot = await _db.TemplateBots.FindAsync(id);
        if (bot is null)
        {
            return NotFound();
        }

        _db.TemplateBots.Remove(bot);
        await _db.SaveChangesAsync();
        this.Notify("Template bot deleted.");
        return RedirectToAction(nameof(Index));
    }

    private static List<string> SplitTriggers(string input) =>
        input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static List<string> ParseTriggers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void FillSlots(CampaignParamInput[] slots, string? json)
    {
        var saved = CampaignParamResolver.ParseList(json);
        for (var i = 0; i < slots.Length && i < saved.Count; i++)
        {
            slots[i] = new CampaignParamInput { Source = saved[i].Source, StaticValue = saved[i].StaticValue };
        }
    }

    private static string SerializeSlots(CampaignParamInput[] slots, int count)
    {
        var result = new List<CampaignParam>(count);
        for (var i = 0; i < count; i++)
        {
            var slot = i < slots.Length ? slots[i] : null;
            result.Add(new CampaignParam { Source = slot?.Source ?? ParamSourceType.StaticText, StaticValue = slot?.StaticValue });
        }

        return CampaignParamResolver.Serialize(result);
    }

    private async Task PopulateOptionsAsync(TemplateBotFormViewModel model)
    {
        model.TemplateOptions = await _db.WhatsappTemplates.AsNoTracking()
            .Where(t => t.Status == TemplateStatus.Approved && t.MetaTemplateId != null)
            .OrderBy(t => t.TemplateName)
            .Select(t => new TemplateOption(t.MetaTemplateId!, t.TemplateName, t.HeaderFormat, t.HeaderParamsCount, t.BodyParamsCount, t.FooterParamsCount))
            .ToListAsync();
    }
}
