using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;

namespace Msgifly.Web.Services.Workspaces;

/// <summary>
/// Resolves the browser session's current workspace from a cookie and sets it on
/// ICurrentWorkspaceAccessor before any controller/EF query runs. This only covers the
/// cookie-authenticated Admin UI path — the WhatsApp webhook receiver and the public API's
/// ApiKey auth resolve their own workspace explicitly (from the inbound WABA id, and from the
/// matched key's WorkspaceId) and simply overwrite whatever this middleware set, so it's safe
/// for this to run unconditionally ahead of every request.
/// </summary>
public class WorkspaceResolutionMiddleware
{
    public const string CookieName = "msgifly_workspace";

    private readonly RequestDelegate _next;

    public WorkspaceResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db, ICurrentWorkspaceAccessor accessor)
    {
        int? resolved = null;

        if (context.Request.Cookies.TryGetValue(CookieName, out var cookieValue) && int.TryParse(cookieValue, out var cookieWorkspaceId))
        {
            var exists = await db.Workspaces.IgnoreQueryFilters()
                .AnyAsync(w => w.Id == cookieWorkspaceId && !w.IsArchived);
            if (exists)
            {
                resolved = cookieWorkspaceId;
            }
        }

        if (resolved is null)
        {
            resolved = await db.Workspaces.IgnoreQueryFilters()
                .Where(w => !w.IsArchived)
                .OrderBy(w => w.Id)
                .Select(w => (int?)w.Id)
                .FirstOrDefaultAsync();
        }

        accessor.WorkspaceId = resolved;
        await _next(context);
    }
}
