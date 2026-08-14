namespace Msgifly.Web.Models.Enums;

public enum TrackingDomainStatus
{
    NotConfigured = 0,
    Pending = 1,
    Active = 2,

    /// <summary>Was Active, now fails verification — a cert renewal or DNS regression, distinct
    /// from Pending (still mid-setup) so the Settings page can flag it more urgently.</summary>
    Failed = 3,
}
