using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Bots;

/// <summary>
/// The keyword-matching engine behind auto-reply bots — mirrors the original's
/// getMessageBotsByRelType/getTemplateBotsByRelType + reply_type re-validation
/// (master doc §5.5). Exact/contains-keyword bots take priority; if none of those match,
/// catch-all (reply_type = 4) bots fire instead. First-message bots only fire on a
/// contact's very first inbound message.
/// </summary>
public class BotMatchingService
{
    private readonly ApplicationDbContext _db;

    public BotMatchingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<BotMatchResult> FindMatchingBotsAsync(ContactType relType, string messageText, bool isFirstMessage)
    {
        var messageBots = await _db.MessageBots.AsNoTracking()
            .Where(b => b.RelType == relType && b.IsActive)
            .ToListAsync();

        var templateBots = await _db.TemplateBots.AsNoTracking()
            .Where(b => b.RelType == relType && b.IsActive)
            .ToListAsync();

        var matchedMessageBots = messageBots.Where(b => Matches(b.ReplyType, b.TriggersJson, messageText, isFirstMessage)).ToList();
        var matchedTemplateBots = templateBots.Where(b => Matches(b.ReplyType, b.TriggersJson, messageText, isFirstMessage)).ToList();

        if (matchedMessageBots.Count == 0 && matchedTemplateBots.Count == 0)
        {
            matchedMessageBots = messageBots.Where(b => b.ReplyType == ReplyType.CatchAll).ToList();
            matchedTemplateBots = templateBots.Where(b => b.ReplyType == ReplyType.CatchAll).ToList();
        }

        return new BotMatchResult(matchedMessageBots, matchedTemplateBots);
    }

    private static bool Matches(ReplyType replyType, string? triggersJson, string messageText, bool isFirstMessage) => replyType switch
    {
        ReplyType.FirstMessage => isFirstMessage,
        ReplyType.ExactMatch => ParseTriggers(triggersJson)
            .Any(t => string.Equals(t, messageText.Trim(), StringComparison.OrdinalIgnoreCase)),
        ReplyType.Contains => ParseTriggers(triggersJson)
            .Any(t => Regex.IsMatch(messageText, $@"\b{Regex.Escape(t)}\b", RegexOptions.IgnoreCase)),
        _ => false, // CatchAll is only ever applied as the fallback, above — never a "specific" match
    };

    private static List<string> ParseTriggers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return (JsonSerializer.Deserialize<List<string>>(json) ?? [])
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
