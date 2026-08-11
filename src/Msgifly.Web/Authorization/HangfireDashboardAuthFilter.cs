using Hangfire.Dashboard;

namespace Msgifly.Web.Authorization;

/// <summary>Restricts /hangfire to signed-in admins only.</summary>
public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.HasClaim(c => c.Type == PermissionAuthorizationHandler.IsAdminClaimType && c.Value == "true");
    }
}
