using Microsoft.AspNetCore.Authorization;

namespace Msgifly.Web.Authorization;

/// <summary>
/// One or more permission strings from a comma-separated policy name, e.g. "contact.create,contact.edit" —
/// satisfied if the user holds ANY of them (matches the original app's checkPermission([...]) OR-array semantics).
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string[] Permissions { get; }

    public PermissionRequirement(string policyName)
    {
        Permissions = policyName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
