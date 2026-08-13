namespace Msgifly.Web.Models.Enums;

public enum ContactGroupType
{
    /// <summary>A fixed, hand-curated member list — built via checkbox-select on Contacts or CSV upload.</summary>
    Static = 0,

    /// <summary>A saved filter (recipient type/status/source) that re-evaluates against current contacts every time it's used.</summary>
    Dynamic = 1,
}
