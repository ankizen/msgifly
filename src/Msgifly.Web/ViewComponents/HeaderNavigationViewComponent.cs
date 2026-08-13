using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Authorization;
using Msgifly.Web.Data;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.ViewComponents;

public class HeaderNavigationViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public HeaderNavigationViewComponent(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var query = _db.Workspaces.AsNoTracking().Where(w => !w.IsArchived);

        // A workspace-scoped user (see WorkspaceUserScopeMiddleware) has nothing to switch to —
        // render just their own workspace as a plain label instead of a dropdown.
        var isAdmin = HttpContext.User.HasClaim(c => c.Type == PermissionAuthorizationHandler.IsAdminClaimType && c.Value == "true");
        var scopedClaim = isAdmin ? null : HttpContext.User.FindFirst(WorkspaceUserScopeMiddleware.ClaimType)?.Value;
        var isScoped = int.TryParse(scopedClaim, out var scopedWorkspaceId);
        if (isScoped)
        {
            query = query.Where(w => w.Id == scopedWorkspaceId);
        }

        var workspaces = await query.OrderBy(w => w.Id).ToListAsync();

        ViewBag.Workspaces = workspaces;
        ViewBag.CurrentWorkspaceId = _workspaceAccessor.WorkspaceId;
        ViewBag.IsScoped = isScoped;
        return View();
    }
}
