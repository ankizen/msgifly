using Msgifly.Web.Authorization;

namespace Msgifly.Web.Services.Workspaces;

/// <summary>
/// Locks a non-admin, workspace-assigned user's request to their one Workspace — runs after
/// WorkspaceResolutionMiddleware's cookie-based default (registered before app.UseAuthentication(),
/// since it needs no signed-in user) and after app.UseAuthentication() populates context.User, so
/// this can read the "workspace_id" claim (see ApplicationUserClaimsPrincipalFactory) and simply
/// override whatever the cookie set. Same "later wins, no-op if not applicable" pattern
/// ApiKeyAuthenticationHandler and WhatsAppWebhookController.Receive() already use to set the
/// accessor for their own request types — this doesn't touch either of those paths since neither
/// principal ever carries this claim.
/// </summary>
public class WorkspaceUserScopeMiddleware
{
    public const string ClaimType = "workspace_id";

    private readonly RequestDelegate _next;

    public WorkspaceUserScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentWorkspaceAccessor accessor)
    {
        var isAdmin = context.User.HasClaim(c => c.Type == PermissionAuthorizationHandler.IsAdminClaimType && c.Value == "true");
        if (!isAdmin)
        {
            var claim = context.User.FindFirst(ClaimType)?.Value;
            if (int.TryParse(claim, out var scopedWorkspaceId))
            {
                accessor.WorkspaceId = scopedWorkspaceId;
            }
        }

        await _next(context);
    }
}
