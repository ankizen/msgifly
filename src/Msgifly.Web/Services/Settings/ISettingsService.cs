namespace Msgifly.Web.Services.Settings;

/// <summary>
/// Typed, cached key-value settings store — one JSON-serialized row per group.
/// The group name is conventionally the type name (e.g. "GeneralSettings"); pass it explicitly
/// so callers control renames independently of the C# type name.
/// </summary>
public interface ISettingsService
{
    Task<T> GetAsync<T>(string group) where T : new();

    Task SaveAsync<T>(string group, T value);
}
