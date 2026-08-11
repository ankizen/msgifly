using System.Security.Claims;
using Msgifly.Web.Authorization;

namespace Msgifly.Web.Extensions;

/// <summary>Same permission logic as PermissionAuthorizationHandler, usable directly in Razor views (e.g. nav visibility).</summary>
public static class ClaimsPrincipalPermissionExtensions
{
    public static bool HasPermission(this ClaimsPrincipal user, params string[] permissions)
    {
        if (user.HasClaim(c => c.Type == PermissionAuthorizationHandler.IsAdminClaimType && c.Value == "true"))
        {
            return true;
        }

        return permissions.Any(p => user.HasClaim(PermissionAuthorizationHandler.PermissionClaimType, p));
    }
}
