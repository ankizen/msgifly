using System.Security.Claims;
using Msgifly.Web.Authorization;

namespace Msgifly.Web.Extensions;

/// <summary>Same permission logic as PermissionAuthorizationHandler, usable directly in Razor views (e.g. nav visibility).</summary>
public static class ClaimsPrincipalPermissionExtensions
{
    public static bool HasPermission(this ClaimsPrincipal user, params string[] permissions)
    {
        if (user.IsMasterAdmin())
        {
            return true;
        }

        return permissions.Any(p => user.HasClaim(PermissionAuthorizationHandler.PermissionClaimType, p));
    }

    /// <summary>The is_admin superuser flag specifically — no per-user/role permission can substitute
    /// for this, unlike HasPermission's normal "role/user grant OR is_admin bypass" check. Used for
    /// surfaces sensitive enough that they should never be delegable via the Roles system at all
    /// (API Keys, which can now drive MCP tools with real send/create/automation power).</summary>
    public static bool IsMasterAdmin(this ClaimsPrincipal user) =>
        user.HasClaim(c => c.Type == PermissionAuthorizationHandler.IsAdminClaimType && c.Value == "true");
}
