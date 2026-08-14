using ModelContextProtocol;
using Msgifly.Web.Authorization;

namespace Msgifly.Web.Services.Mcp;

/// <summary>
/// Every MCP tool call already passed transport-level auth (app.MapMcp(...).RequireAuthorization
/// in Program.cs — some valid, non-revoked API key), but that only proves who's calling, not what
/// they're allowed to do. Each tool method calls this first with the one scope it needs, mirroring
/// the manual `User.HasApiScope(...) -> Forbid()` check every /api/v1/* controller action already
/// does — McpException is how the SDK surfaces a clean tool-call failure (readable by the calling
/// agent) instead of an unhandled 500.
/// </summary>
public static class McpScopeGuard
{
    public static void RequireScope(this IHttpContextAccessor httpContextAccessor, string scope)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null || !user.HasApiScope(scope))
        {
            throw new McpException($"This API key is missing the '{scope}' scope. Grant it from Admin → API Keys.");
        }
    }
}
