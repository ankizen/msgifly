using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Groups;

/// <summary>Deserialized shape of ContactGroup.FilterJson for a Dynamic group — null/empty means "any".</summary>
public class DynamicGroupFilter
{
    public ContactType? RelType { get; set; }
    public List<int> StatusIds { get; set; } = [];
    public List<int> SourceIds { get; set; } = [];
}
