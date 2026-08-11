using Microsoft.AspNetCore.Authorization;

namespace Msgifly.Web.Authorization;

/// <summary>A single permission string, e.g. "contact.view" — matches the original app's permission naming.</summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}
