using System.Text.Json;
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
public class MessageBotsController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public MessageBotsController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "message_bot.view")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var query = _db.MessageBots.AsNoTracking().OrderByDescending(b => b.CreatedAt);
        return View(await PagedList<MessageBot>.CreateAsync(query, page, PageSize));
    }

    [Authorize(Policy = "message_bot.create,message_bot.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        var model = new MessageBotFormViewModel();

        if (id is not null)
        {
            var bot = await _db.MessageBots.FindAsync(id.Value);
            if (bot is null)
            {
                return NotFound();
            }

            model = new MessageBotFormViewModel
            {
                Id = bot.Id,
                Name = bot.Name,
                RelType = bot.RelType,
                ReplyType = bot.ReplyType,
                TriggersInput = string.Join(", ", ParseTriggers(bot.TriggersJson)),
                ReplyText = bot.ReplyText,
                HeaderText = bot.HeaderText,
                FooterText = bot.FooterText,
                IsActive = bot.IsActive,
            };
        }

        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "message_bot.create,message_bot.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(MessageBotFormViewModel model)
    {
        if (model.ReplyType != Models.Enums.ReplyType.FirstMessage
            && model.ReplyType != Models.Enums.ReplyType.CatchAll
            && string.IsNullOrWhiteSpace(model.TriggersInput))
        {
            ModelState.AddModelError(nameof(model.TriggersInput), "Enter at least one trigger keyword for this mode.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var triggersJson = JsonSerializer.Serialize(SplitTriggers(model.TriggersInput));

        if (model.Id is null)
        {
            _db.MessageBots.Add(new MessageBot
            {
                WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
                Name = model.Name,
                RelType = model.RelType,
                ReplyType = model.ReplyType,
                TriggersJson = triggersJson,
                ReplyText = model.ReplyText,
                HeaderText = model.HeaderText,
                FooterText = model.FooterText,
                IsActive = model.IsActive,
            });
            this.Notify("Message bot created.");
        }
        else
        {
            var bot = await _db.MessageBots.FindAsync(model.Id.Value);
            if (bot is null)
            {
                return NotFound();
            }

            bot.Name = model.Name;
            bot.RelType = model.RelType;
            bot.ReplyType = model.ReplyType;
            bot.TriggersJson = triggersJson;
            bot.ReplyText = model.ReplyText;
            bot.HeaderText = model.HeaderText;
            bot.FooterText = model.FooterText;
            bot.IsActive = model.IsActive;
            bot.UpdatedAt = DateTime.UtcNow;
            this.Notify("Message bot updated.");
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "message_bot.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var bot = await _db.MessageBots.FindAsync(id);
        if (bot is null)
        {
            return NotFound();
        }

        _db.MessageBots.Remove(bot);
        await _db.SaveChangesAsync();
        this.Notify("Message bot deleted.");
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
}
