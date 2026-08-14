using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.ApiKeys;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

/// <summary>
/// A key granted here can now drive the MCP tools (Services/Mcp/*) — create templates that go
/// live on Meta, build automations, send real WhatsApp messages. That's too much power to leave
/// delegable through the normal per-role permission system, so every action here requires the
/// is_admin superuser flag specifically (MasterAdminOnly, registered in Program.cs), not the usual
/// "role/user grant OR is_admin" permission check the rest of the Admin area uses.
/// </summary>
[Area("Admin")]
[Authorize(Policy = "MasterAdminOnly")]
public class ApiKeysController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public ApiKeysController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    public async Task<IActionResult> Index()
    {
        var keys = await _db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync();
        return View(keys);
    }

    public IActionResult Create() => View(new ApiKeyFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ApiKeyFormViewModel model)
    {
        var validScopes = model.Scopes.Where(s => ApiScopes.All.Contains(s)).ToList();
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var generated = ApiKeyGenerator.Generate();
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;

        var apiKey = new ApiKey
        {
            WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
            Name = model.Name.Trim(),
            KeyPrefix = generated.DisplayPrefix,
            KeyHash = generated.Hash,
            ScopesCsv = string.Join(',', validScopes),
            CreatedByUserId = userId,
        };
        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync();

        // Shown exactly once — never persisted, never retrievable again after this response.
        TempData["NewApiKeyPlaintext"] = generated.Plaintext;
        return RedirectToAction(nameof(Created), new { id = apiKey.Id });
    }

    public async Task<IActionResult> Created(int id)
    {
        var apiKey = await _db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id);
        if (apiKey is null)
        {
            return NotFound();
        }

        var plaintext = TempData["NewApiKeyPlaintext"] as string;
        if (string.IsNullOrEmpty(plaintext))
        {
            // Reload/back-navigation after the one-time TempData was consumed — nothing secret to show.
            this.Notify("That key's plaintext was already shown once and can't be displayed again.", "danger");
            return RedirectToAction(nameof(Index));
        }

        ViewData["Plaintext"] = plaintext;
        return View(apiKey);
    }

    /// <summary>On/off switch — Revoked is reversible (unlike Delete below), for the common case of
    /// temporarily pausing a key (e.g. rotating out an MCP client) without losing its scopes/name
    /// and having to reissue a whole new plaintext secret.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var apiKey = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id);
        if (apiKey is null)
        {
            return NotFound();
        }

        apiKey.RevokedAt = apiKey.RevokedAt is null ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync();

        this.Notify(apiKey.RevokedAt is null ? "API key re-enabled." : "API key revoked — anything using it will stop working immediately.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var apiKey = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id);
        if (apiKey is null)
        {
            return NotFound();
        }

        _db.ApiKeys.Remove(apiKey);
        await _db.SaveChangesAsync();

        this.Notify("API key deleted.");
        return RedirectToAction(nameof(Index));
    }
}
