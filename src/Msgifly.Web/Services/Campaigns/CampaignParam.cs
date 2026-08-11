using System.Text.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Campaigns;

/// <summary>One template placeholder's configured value source, stored as JSON on Campaign.*ParamsJson.</summary>
public class CampaignParam
{
    public ParamSourceType Source { get; set; } = ParamSourceType.StaticText;
    public string? StaticValue { get; set; }
}

public static class CampaignParamResolver
{
    public static List<CampaignParam> ParseList(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<CampaignParam>>(json) ?? [];

    public static string Serialize(List<CampaignParam> parameters) => JsonSerializer.Serialize(parameters);

    /// <summary>Resolves a param's value for a specific recipient — literal text, or one of the contact's own fields.</summary>
    public static string Resolve(CampaignParam param, Contact contact) => param.Source switch
    {
        ParamSourceType.ContactFirstName => contact.FirstName,
        ParamSourceType.ContactLastName => contact.LastName,
        ParamSourceType.ContactFullName => contact.FullName,
        ParamSourceType.ContactPhone => contact.Phone,
        ParamSourceType.ContactEmail => contact.Email ?? string.Empty,
        ParamSourceType.ContactCompany => contact.Company ?? string.Empty,
        _ => param.StaticValue ?? string.Empty,
    };

    public static List<string> ResolveAll(string? json, Contact contact) =>
        ParseList(json).Select(p => Resolve(p, contact)).ToList();
}
