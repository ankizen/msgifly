namespace Msgifly.Web.Models.Enums;

/// <summary>
/// Where a template placeholder's value comes from for a given campaign — either the same
/// literal text for every recipient, or a per-contact field for basic personalization
/// (a smaller, fixed alternative to the original's pluggable merge-field registry — see
/// master doc §5.4 for the full system this simplifies).
/// </summary>
public enum ParamSourceType
{
    StaticText = 0,
    ContactFirstName = 1,
    ContactLastName = 2,
    ContactFullName = 3,
    ContactPhone = 4,
    ContactEmail = 5,
    ContactCompany = 6,
}
