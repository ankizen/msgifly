namespace Msgifly.Web.Models.Entities;

/// <summary>
/// One row per settings group, storing the whole group as JSON in <see cref="Value"/>.
/// Backs <see cref="Msgifly.Web.Services.Settings.ISettingsService"/>.
/// </summary>
public class AppSetting
{
    public int Id { get; set; }
    public string Group { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
