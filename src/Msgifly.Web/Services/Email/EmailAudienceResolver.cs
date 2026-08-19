using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email;

/// <summary>
/// Turns an EmailCampaign's targeting fields into a flat subscriber-id list — mirrors
/// ContactGroupResolver's role for WhatsApp campaigns. SelectAll bypasses the Include lists/tags
/// (every bulk-sendable subscriber in the workspace), but Exclude still subtracts even in that
/// mode — a suppression list should apply regardless of how the rest of the audience was picked.
/// </summary>
public class EmailAudienceResolver
{
    private static readonly EmailSubscriberStatus[] BulkSendableStatuses =
        [EmailSubscriberStatus.Subscribed, EmailSubscriberStatus.Transactional];

    private readonly ApplicationDbContext _db;

    public EmailAudienceResolver(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<int>> ResolveSubscriberIdsAsync(EmailCampaign campaign)
    {
        var query = _db.EmailSubscribers.AsNoTracking()
            .Where(s => s.WorkspaceId == campaign.WorkspaceId && BulkSendableStatuses.Contains(s.Status));

        if (!campaign.SelectAll)
        {
            var includeListIds = ParseIds(campaign.IncludeListIdsJson);
            var includeTagIds = ParseIds(campaign.IncludeTagIdsJson);

            if (includeListIds.Count == 0 && includeTagIds.Count == 0)
            {
                return [];
            }

            query = query.Where(s =>
                (includeListIds.Count > 0 && _db.EmailSubscriberLists.Any(l => l.SubscriberId == s.Id && includeListIds.Contains(l.ListId))) ||
                (includeTagIds.Count > 0 && _db.EmailSubscriberTags.Any(t => t.SubscriberId == s.Id && includeTagIds.Contains(t.TagId))));
        }

        var excludeListIds = ParseIds(campaign.ExcludeListIdsJson);
        if (excludeListIds.Count > 0)
        {
            query = query.Where(s => !_db.EmailSubscriberLists.Any(l => l.SubscriberId == s.Id && excludeListIds.Contains(l.ListId)));
        }

        var excludeTagIds = ParseIds(campaign.ExcludeTagIdsJson);
        if (excludeTagIds.Count > 0)
        {
            query = query.Where(s => !_db.EmailSubscriberTags.Any(t => t.SubscriberId == s.Id && excludeTagIds.Contains(t.TagId)));
        }

        return await query.Select(s => s.Id).Distinct().ToListAsync();
    }

    private static List<int> ParseIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
