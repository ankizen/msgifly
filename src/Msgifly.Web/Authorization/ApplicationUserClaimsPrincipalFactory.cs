using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Authorization;

/// <summary>
/// Adds the "is_admin" claim (from ApplicationUser.IsAdmin) and, when set, the "workspace_id"
/// claim (from ApplicationUser.WorkspaceId — see WorkspaceUserScopeMiddleware) to the signed-in
/// principal, since Identity only auto-populates role/permission claims (see
/// PermissionAuthorizationHandler) — arbitrary user columns need to be projected explicitly. A
/// changed WorkspaceId assignment, like IsAdmin, only takes effect on the user's next login —
/// claims are baked into the auth cookie at sign-in, not re-read from the DB every request.
/// </summary>
public class ApplicationUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<int>>
{
    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<int>> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(
            PermissionAuthorizationHandler.IsAdminClaimType, user.IsAdmin ? "true" : "false"));
        identity.AddClaim(new Claim(ClaimTypes.GivenName, user.FirstName));
        identity.AddClaim(new Claim(ClaimTypes.Surname, user.LastName));

        if (user.WorkspaceId is not null)
        {
            identity.AddClaim(new Claim(WorkspaceUserScopeMiddleware.ClaimType, user.WorkspaceId.Value.ToString()));
        }

        return identity;
    }
}
