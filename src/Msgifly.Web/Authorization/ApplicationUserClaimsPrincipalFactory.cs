using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Msgifly.Web.Models.Entities;

namespace Msgifly.Web.Authorization;

/// <summary>
/// Adds the "is_admin" claim (from ApplicationUser.IsAdmin) to the signed-in principal, since
/// Identity only auto-populates role/permission claims (see PermissionAuthorizationHandler) —
/// arbitrary user columns like IsAdmin need to be projected explicitly.
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

        return identity;
    }
}
