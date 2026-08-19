using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.EmailAutomations;

namespace Msgifly.Web.Models.ViewModels;

public class EmailAutomationFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string TriggerType { get; set; } = nameof(EmailAutomationTriggerType.SubscriberAdded);

    /// <summary>SubscriberAdded/ListApplied scoping — null means "any list".</summary>
    public int? ListId { get; set; }

    /// <summary>TagApplied scoping — null means "any tag".</summary>
    public int? TagId { get; set; }

    public bool IsActive { get; set; }

    /// <summary>The whole step tree, as JSON produced by the email-automation-builder's Alpine component.</summary>
    [Required]
    public string StepsJson { get; set; } = "[]";

    public static EmailAutomationFormViewModel FromEntity(EmailAutomation automation, List<EmailAutomationStep> steps)
    {
        var model = new EmailAutomationFormViewModel
        {
            Id = automation.Id,
            Name = automation.Name,
            Description = automation.Description,
            TriggerType = automation.TriggerType.ToString(),
            IsActive = automation.IsActive,
        };

        if (automation.TriggerType is EmailAutomationTriggerType.SubscriberAdded or EmailAutomationTriggerType.ListApplied)
        {
            model.ListId = SafeDeserialize<ListScopedTriggerConfig>(automation.TriggerConfigJson)?.ListId;
        }
        else if (automation.TriggerType == EmailAutomationTriggerType.TagApplied)
        {
            model.TagId = SafeDeserialize<TagAppliedTriggerConfig>(automation.TriggerConfigJson)?.TagId;
        }

        model.StepsJson = JsonSerializer.Serialize(BuildTree(steps, null, null), new JsonSerializerOptions(JsonSerializerOptions.Web));
        return model;
    }

    private static List<object> BuildTree(List<EmailAutomationStep> allSteps, int? parentId, string? branch)
    {
        var scoped = allSteps
            .Where(s => s.ParentStepId == parentId && s.Branch == branch)
            .OrderBy(s => s.Position);

        var result = new List<object>();
        foreach (var step in scoped)
        {
            var config = JsonSerializer.Deserialize<JsonElement>(step.StepConfigJson);
            if (step.StepType == EmailAutomationStepType.Condition)
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
