using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.Automations;

namespace Msgifly.Web.Models.ViewModels;

/// <summary>Wire shape for one node in the client-authored step tree — see Views/Automations/Save.cshtml's Alpine builder. Yes/No are only ever populated one level deep (a Condition's children can't themselves be Conditions) — a deliberate scope cut from a full recursive canvas builder.</summary>
public class AutomationStepNode
{
    public string Type { get; set; } = string.Empty;
    public JsonElement Config { get; set; }
    public List<AutomationStepNode>? Yes { get; set; }
    public List<AutomationStepNode>? No { get; set; }
}

public class AutomationFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string TriggerType { get; set; } = nameof(AutomationTriggerType.InboundMessage);

    public string? KeywordsCsv { get; set; }
    public string KeywordMatchType { get; set; } = "contains";
    public bool KeywordCaseSensitive { get; set; }
    public string? InteractiveReplyIdsCsv { get; set; }

    public bool IsActive { get; set; }

    /// <summary>The whole step tree, as JSON produced by the builder's Alpine component.</summary>
    [Required]
    public string StepsJson { get; set; } = "[]";

    public static AutomationFormViewModel FromEntity(Automation automation, List<AutomationStep> steps)
    {
        var model = new AutomationFormViewModel
        {
            Id = automation.Id,
            Name = automation.Name,
            Description = automation.Description,
            TriggerType = automation.TriggerType.ToString(),
            IsActive = automation.IsActive,
        };

        if (automation.TriggerType == AutomationTriggerType.KeywordMatch)
        {
            var cfg = SafeDeserialize<KeywordMatchTriggerConfig>(automation.TriggerConfigJson);
            if (cfg is not null)
            {
                model.KeywordsCsv = string.Join(", ", cfg.Keywords);
                model.KeywordMatchType = cfg.MatchType;
                model.KeywordCaseSensitive = cfg.CaseSensitive;
            }
        }
        else if (automation.TriggerType == AutomationTriggerType.InteractiveReply)
        {
            var cfg = SafeDeserialize<InteractiveReplyTriggerConfig>(automation.TriggerConfigJson);
            if (cfg is not null)
            {
                model.InteractiveReplyIdsCsv = string.Join(", ", cfg.ReplyIds);
            }
        }

        model.StepsJson = JsonSerializer.Serialize(BuildTree(steps, null, null), new JsonSerializerOptions(JsonSerializerOptions.Web));
        return model;
    }

    private static List<object> BuildTree(List<AutomationStep> allSteps, int? parentId, string? branch)
    {
        var scoped = allSteps
            .Where(s => s.ParentStepId == parentId && s.Branch == branch)
            .OrderBy(s => s.Position);

        var result = new List<object>();
        foreach (var step in scoped)
        {
            var config = JsonSerializer.Deserialize<JsonElement>(step.StepConfigJson);
            if (step.StepType == AutomationStepType.Condition)
            {
                result.Add(new
                {
                    type = step.StepType.ToString(),
                    config,
                    yes = BuildTree(allSteps, step.Id, "Yes"),
                    no = BuildTree(allSteps, step.Id, "No"),
                });
            }
            else
            {
                result.Add(new { type = step.StepType.ToString(), config });
            }
        }

        return result;
    }

    private static T? SafeDeserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerOptions.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
