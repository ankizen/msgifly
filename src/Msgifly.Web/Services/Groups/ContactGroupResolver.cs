using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;

namespace Msgifly.Web.Services.Groups;

/// <summary>
/// Turns a ContactGroup (Static member list or Dynamic filter) into a flat, current contact-id
/// list — the one place both GroupsController (live member counts) and CampaignsController
/// (resolving "use this group" into the same SelectedContactIds path already used for hand-picked
/// recipients) go to avoid re-implementing the Static/Dynamic branching twice.
/// </summary>
public class ContactGroupResolver
{
    private readonly ApplicationDbContext _db;

    public ContactGroupResolver(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<int>> ResolveContactIdsAsync(ContactGroup group)
    {
        if (group.Type == Models.Enums.ContactGroupType.Static)
        {
            return await _db.ContactGroupMembers.AsNoTracking()
                .Where(m => m.GroupId == group.Id)
                .Select(m => m.ContactId)
                .ToListAsync();
        }

        var filter = ParseFilter(group.FilterJson);
        var query = _db.Contacts.AsNoTracking().Where(c => c.IsEnabled);

        if (filter.RelType is not null)
        {
            query = query.Where(c => c.Type == filter.RelType);
        }

        if (filter.StatusIds.Count > 0)
        {
            query = query.Where(c => filter.StatusIds.Contains(c.StatusId));
        }

        if (filter.SourceIds.Count > 0)
        {
            query = query.Where(c => filter.SourceIds.Contains(c.SourceId));
        }

        return await query.Select(c => c.Id).ToListAsync();
    }

    public async Task<int> CountAsync(ContactGroup group) => (await ResolveContactIdsAsync(group)).Count;

    public static DynamicGroupFilter ParseFilter(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new DynamicGroupFilter();
        }

        try
        {
            return JsonSerializer.Deserialize<DynamicGroupFilter>(json) ?? new DynamicGroupFilter();
        }
        catch (JsonException)
        {
            return new DynamicGroupFilter();
        }
    }
}
