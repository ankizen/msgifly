using Microsoft.AspNetCore.Identity;

namespace Msgifly.Web.Models.Entities;

public class ApplicationUser : IdentityUser<int>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? DefaultLanguage { get; set; }
    public string? ProfileImageUrl { get; set; }

    /// <summary>Superuser flag — bypasses all permission checks (mirrors the original is_admin column).</summary>
    public bool IsAdmin { get; set; }

    public bool Active { get; set; } = true;
    public DateTime? LastLogin { get; set; }
    public DateTime? BannedAt { get; set; }

    /// <summary>Locks a non-admin user to exactly one Workspace — null means unscoped (sees/switches
    /// every Workspace, subject to normal permission checks). Always ignored when IsAdmin is true.</summary>
    public int? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public ICollection<Contact> AssignedContacts { get; set; } = new List<Contact>();
}
