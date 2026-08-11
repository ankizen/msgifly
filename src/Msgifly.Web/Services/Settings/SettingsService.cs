using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;

namespace Msgifly.Web.Services.Settings;

public class SettingsService : ISettingsService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public SettingsService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<T> GetAsync<T>(string group) where T : new()
    {
        var cacheKey = CacheKey(group);
        if (_cache.TryGetValue(cacheKey, out T? cached) && cached is not null)
        {
            return cached;
        }

        var row = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Group == group);
        var value = row?.Value is not null
            ? JsonSerializer.Deserialize<T>(row.Value) ?? new T()
            : new T();

        _cache.Set(cacheKey, value, CacheDuration);
        return value;
    }

    public async Task SaveAsync<T>(string group, T value)
    {
        var row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Group == group);
        var json = JsonSerializer.Serialize(value);

        if (row is null)
        {
            _db.AppSettings.Add(new AppSetting { Group = group, Value = json });
        }
        else
        {
            row.Value = json;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        _cache.Remove(CacheKey(group));
    }

    private static string CacheKey(string group) => $"settings.{group}";
}
