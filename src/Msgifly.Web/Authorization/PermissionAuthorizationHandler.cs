using Microsoft.AspNetCore.Authorization;

namespace Msgifly.Web.Authorization;

/// <summary>
/// Checks the "permission" claim against the requirement. Claims come from two places, both
/// populated automatically into the signed-in user's ClaimsPrincipal by ASP.NET Core Identity:
/// role claims (RoleManager.AddClaimAsync) and per-user claims (UserManager.AddClaimAsync) —
/// this mirrors the original's "role permissions UNION per-user extra permissions" model.
/// The "is_admin" claim bypasses every check, mirroring the original's is_admin superuser flag.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    public const string PermissionClaimType = "permission";
    public const string IsAdminClaimType = "is_admin";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.HasClaim(c => c.Type == IsAdminClaimType && c.Value == "true"))
        {
            context.Succeed(requirement);
        }
        else if (requirement.Permissions.Any(p => context.User.HasClaim(PermissionClaimType, p)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
