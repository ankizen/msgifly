using System.Text.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;

namespace Msgifly.Web.Services.Automations;

/// <summary>
/// Converts the JSON tree shape the automation canvas (and now MCP tools) produce into persisted
/// <see cref="AutomationStep"/> rows, and builds a trigger's config JSON from its scalar fields.
/// Extracted from AutomationsController so both the browser form POST and MCP's create_automation
/// tool go through the exact same validation/persistence logic instead of two copies drifting
/// apart over time.
/// </summary>
public static class AutomationTreeBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);

    public static string BuildTriggerConfigJson(
        AutomationTriggerType triggerType,
        string? keywordsCsv,
        string? keywordMatchType,
        bool keywordCaseSensitive,
        string? interactiveReplyIdsCsv,
        string? leadFormId)
    {
        switch (triggerType)
        {
            case AutomationTriggerType.KeywordMatch:
                var keywords = (keywordsCsv ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                return JsonSerializer.Serialize(new KeywordMatchTriggerConfig
                {
                    Keywords = keywords,
                    MatchType = keywordMatchType ?? "contains",
                    CaseSensitive = keywordCaseSensitive,
                }, JsonOptions);

            case AutomationTriggerType.InteractiveReply:
                var replyIds = (interactiveReplyIdsCsv ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                return JsonSerializer.Serialize(new InteractiveReplyTriggerConfig { ReplyIds = replyIds }, JsonOptions);

            case AutomationTriggerType.FacebookLeadReceived:
                return JsonSerializer.Serialize(new FacebookLeadFormTriggerConfig
                {
                    FormId = string.IsNullOrWhiteSpace(leadFormId) ? null : leadFormId.Trim(),
                }, JsonOptions);

            default:
                return "{}";
        }
    }

    /// <summary>Arbitrary nesting is fine — the canvas builder represents this as a graph (Condition
    /// nodes with two outputs, each potentially leading to further Conditions), which the engine's
    /// recursive step-walker already executes correctly regardless of depth. Throws
    /// <see cref="ArgumentException"/> on an unrecognized step type.</summary>
    public static void ValidateTree(List<AutomationStepNode> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            if (!Enum.TryParse<AutomationStepType>(node.Type, ignoreCase: true, out var stepType))
            {
                throw new ArgumentException($"Unknown step type: {node.Type}");
            }

            if (stepType == AutomationStepType.Condition)
            {
                if (node.Yes is not null) ValidateTree(node.Yes, depth + 1);
                if (node.No is not null) ValidateTree(node.No, depth + 1);
            }
        }
    }

    public static void FlattenTree(List<AutomationStepNode> nodes, int automationId, int? parentStepId, string? branch, List<AutomationStep> output)
    {
        var position = 0;
        foreach (var node in nodes)
        {
            Enum.TryParse<AutomationStepType>(node.Type, ignoreCase: true, out var stepType);

            var step = new AutomationStep
            {
                AutomationId = automationId,
                ParentStepId = parentStepId,
                Branch = branch,
                StepType = stepType,
                StepConfigJson = node.Config.ValueKind == JsonValueKind.Undefined ? "{}" : node.Config.GetRawText(),
                Position = position++,
            };
            output.Add(step);

            if (stepType == AutomationStepType.Condition)
            {
                // Children reference this step by Id, which EF only assigns after SaveChanges —
                // AddRange + SaveChanges in the caller persists parents and children in one
                // batch, but the self-referencing FK needs the parent's Id known first. EF Core's
                // change tracker resolves this automatically via navigation fixup as long as
                // ParentStep is set instead of ParentStepId directly for not-yet-saved parents.
                if (node.Yes is not null)
                {
                    var yesChildren = new List<AutomationStep>();
                    FlattenTree(node.Yes, automationId, null, "Yes", yesChildren);
                    foreach (var child in yesChildren)
                    {
                        child.ParentStep = step;
                    }

                    output.AddRange(yesChildren);
                }

                if (node.No is not null)
                {
                    var noChildren = new List<AutomationStep>();
                    FlattenTree(node.No, automationId, null, "No", noChildren);
                    foreach (var child in noChildren)
                    {
                        child.ParentStep = step;
                    }

                    output.AddRange(noChildren);
                }
            }
        }
    }
}
