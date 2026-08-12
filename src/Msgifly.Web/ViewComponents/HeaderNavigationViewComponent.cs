using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        var workspaces = await _db.Workspaces.AsNoTracking()
            .Where(w => !w.IsArchived)
            .OrderBy(w => w.Id)
            .ToListAsync();

        ViewBag.Workspaces = workspaces;
        ViewBag.CurrentWorkspaceId = _workspaceAccessor.WorkspaceId;
        return View();
    }
}
