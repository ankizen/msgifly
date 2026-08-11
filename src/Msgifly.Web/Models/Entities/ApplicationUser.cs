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

    public string FullName => $"{FirstName} {LastName}".Trim();

    public ICollection<Contact> AssignedContacts { get; set; } = new List<Contact>();
}
