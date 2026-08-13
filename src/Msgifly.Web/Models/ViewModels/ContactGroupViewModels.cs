using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.ViewModels;

/// <summary>A group, for the "Add to group" bulk action on the Contacts list (Static only) and the "Use a saved group" campaign recipient option (either type).</summary>
public record GroupOption(int Id, string Name, string Type = "Static");

public class GroupListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ContactGroupType Type { get; set; }
    public int MemberCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Backs Groups/Save — Type picks which half of the form actually matters; the other half is ignored server-side.</summary>
public class GroupFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public ContactGroupType Type { get; set; } = ContactGroupType.Dynamic;

    // Dynamic filter — richer than a campaign's inline filter (multi-status, multi-source) since
    // it's saved and reused, not just a one-off narrowing.
    public ContactType? FilterRelType { get; set; }
    public List<int> FilterStatusIds { get; set; } = [];
    public List<int> FilterSourceIds { get; set; } = [];

    public List<SelectListItem> StatusOptions { get; set; } = [];
    public List<SelectListItem> SourceOptions { get; set; } = [];
}

/// <summary>Backs Groups/Members — managing a Static group's fixed contact list.</summary>
public class GroupMembersViewModel
{
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public List<ContactOption> Members { get; set; } = [];
    public List<ContactOption> ContactOptions { get; set; } = [];
}

/// <summary>Result of a group CSV upload — phone numbers matched against existing contacts.</summary>
public class GroupCsvUploadResult
{
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int Matched { get; set; }
    public int AlreadyMember { get; set; }
    public List<string> Unmatched { get; set; } = [];
}
