using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Authorization;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

/// <summary>
/// A "business" in the Tech Provider sense — the user's own separate businesses each get their
/// own Workspace, connected to its own WhatsApp Business Account, with its own Contacts,
/// Templates, Campaigns, Chat and Automations (see ApplicationDbContext's query filters).
/// Unscoped users (IsAdmin, or ApplicationUser.WorkspaceId is null) can access every Workspace —
/// they switch which one is "current" via the header dropdown, backed by a cookie. A user with
/// WorkspaceId set is locked to exactly that one (see WorkspaceUserScopeMiddleware) — the
/// switcher doesn't even render for them (HeaderNavigationViewComponent).
/// </summary>
[Area("Admin")]
[Authorize]
public class WorkspacesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public WorkspacesController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "workspace.view")]
    public async Task<IActionResult> Index()
    {
        var query = _db.Workspaces.Where(w => !w.IsArchived);

        // A workspace-scoped user (see WorkspaceUserScopeMiddleware) shouldn't see that other
        // businesses even exist — just their own settings.
        var scopedWorkspaceId = ScopedWorkspaceId();
        if (scopedWorkspaceId is not null)
        {
            query = query.Where(w => w.Id == scopedWorkspaceId);
        }

        var workspaces = await query.OrderBy(w => w.Id).ToListAsync();
        ViewData["CurrentWorkspaceId"] = _workspaceAccessor.WorkspaceId;
        return View(workspaces);
    }

    [Authorize(Policy = "workspace.create")]
    public IActionResult Create() => View();

    [HttpPost]
    [Authorize(Policy = "workspace.create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(name), "Give this business a name.");
            return View();
        }

        var workspace = new Workspace { Name = name.Trim() };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        _db.Sources.AddRange(
            new Source { WorkspaceId = workspace.Id, Name = "Facebook" },
            new Source { WorkspaceId = workspace.Id, Name = "WhatsApp" },
            new Source { WorkspaceId = workspace.Id, Name = "Website" });
        _db.Statuses.AddRange(
            new Status { WorkspaceId = workspace.Id, Name = "New", Color = "#4CAF50", IsDefault = true },
            new Status { WorkspaceId = workspace.Id, Name = "In Progress", Color = "#2196F3" },
            new Status { WorkspaceId = workspace.Id, Name = "Contacted", Color = "#FFC107" },
            new Status { WorkspaceId = workspace.Id, Name = "Qualified", Color = "#9C27B0" },
            new Status { WorkspaceId = workspace.Id, Name = "Closed", Color = "#F44336" });
        await _db.SaveChangesAsync();

        SwitchCookie(workspace.Id);
        this.Notify($"\"{workspace.Name}\" created. Connect its WhatsApp Business Account to get started.");
        return RedirectToAction("Index", "Waba");
    }

    [HttpPost]
    [Authorize(Policy = "workspace.view")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(int id, string? returnUrl)
    {
        var scopedWorkspaceId = ScopedWorkspaceId();
        if (scopedWorkspaceId is not null && scopedWorkspaceId != id)
        {
            // WorkspaceUserScopeMiddleware would silently re-clamp this back on the very next
            // request anyway (it overrides the cookie unconditionally) — this just avoids a
            // confusing dead-end where the switch appears to "work" for one request.
            return Forbid();
        }

        var exists = await _db.Workspaces.AnyAsync(w => w.Id == id && !w.IsArchived);
        if (!exists)
        {
            return NotFound();
        }

        SwitchCookie(id);
        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "/Admin/Dashboard" : returnUrl);
    }

    [HttpPost]
    [Authorize(Policy = "workspace.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(int id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            this.Notify("Name can't be empty.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var workspace = await _db.Workspaces.FindAsync(id);
        if (workspace is null)
        {
            return NotFound();
        }

        workspace.Name = name.Trim();
        workspace.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        this.Notify("Workspace renamed.");
        return RedirectToAction(nameof(Index));
    }

    private int? ScopedWorkspaceId()
    {
        if (User.HasClaim(c => c.Type == PermissionAuthorizationHandler.IsAdminClaimType && c.Value == "true"))
        {
            return null;
        }

        var claim = User.FindFirst(WorkspaceUserScopeMiddleware.ClaimType)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    private void SwitchCookie(int workspaceId)
    {
        Response.Cookies.Append(WorkspaceResolutionMiddleware.CookieName, workspaceId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
        });
    }
}
