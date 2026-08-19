using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Definition only — values live in EmailSubscriber.CustomFieldsJson, keyed by Key.</summary>
public class EmailCustomField
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }

    /// <summary>Slug used as the JSON key into EmailSubscriber.CustomFieldsJson.</summary>
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public EmailCustomFieldType FieldType { get; set; } = EmailCustomFieldType.Text;

    /// <summary>Dropdown only — JSON array of option strings.</summary>
    public string? OptionsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
