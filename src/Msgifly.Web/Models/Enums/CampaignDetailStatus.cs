namespace Msgifly.Web.Models.Enums;

/// <summary>
/// The original PHP app left the "success" value of campaign_details.status implicit
/// (only ever confirmed 0 = failed). Made explicit here.
/// </summary>
public enum CampaignDetailStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}
