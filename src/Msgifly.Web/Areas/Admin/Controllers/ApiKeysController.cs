using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.ApiKeys;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class ApiKeysController : Controller
{
    private readonly ApplicationDbContext _db;

    public ApiKeysController(ApplicationDbContext db)
    {
        _db = db;
    }

    [Authorize(Policy = "api_key.view")]
    public async Task<IActionResult> Index()
    {
        var keys = await _db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync();
        return View(keys);
    }

    [Authorize(Policy = "api_key.create")]
    public IActionResult Create() => View(new ApiKeyFormViewModel());

    [HttpPost]
    [Authorize(Policy = "api_key.create")]
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

    [Authorize(Policy = "api_key.create")]
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

    [HttpPost]
    [Authorize(Policy = "api_key.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(int id)
    {
        var apiKey = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id);
        if (apiKey is null)
        {
            return NotFound();
        }

        apiKey.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        this.Notify("API key revoked.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "api_key.delete")]
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
