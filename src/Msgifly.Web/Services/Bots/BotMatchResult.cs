using Msgifly.Web.Models.Entities;

namespace Msgifly.Web.Services.Bots;

public record BotMatchResult(List<MessageBot> MessageBots, List<TemplateBot> TemplateBots)
{
    public bool Any => MessageBots.Count > 0 || TemplateBots.Count > 0;
}
