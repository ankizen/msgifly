using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Msgifly.Web.Authorization;

/// <summary>
/// Lets any [Authorize(Policy = "contact.view")] attribute work without pre-registering all
/// ~45 permission policies in Program.cs — any policy name that isn't otherwise registered is
/// treated as a permission string and turned into a PermissionRequirement on the fly.
/// </summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var existing = await _fallback.GetPolicyAsync(policyName);
        if (existing is not null)
        {
            return existing;
        }

        return new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
    }
}
