using System.Text.Json;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.EmailAutomations;

/// <summary>
/// Converts the JSON tree shape the email-automation-builder canvas produces into persisted
/// EmailAutomationStep rows. Independent copy of AutomationTreeBuilder's exact design (arbitrary
/// Condition nesting, same FlattenTree buffer-per-recursive-call fix) — no shared code with the
/// WhatsApp stack.
/// </summary>
public static class EmailAutomationTreeBuilder
{
    public static void ValidateTree(List<EmailAutomationStepNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!Enum.TryParse<EmailAutomationStepType>(node.Type, ignoreCase: true, out var stepType))
            {
                throw new ArgumentException($"Unknown step type: {node.Type}");
            }

            if (stepType == EmailAutomationStepType.Condition)
            {
                if (node.Yes is not null) ValidateTree(node.Yes);
                if (node.No is not null) ValidateTree(node.No);
            }
        }
    }

    public static void FlattenTree(List<EmailAutomationStepNode> nodes, int automationId, int? parentStepId, string? branch, List<EmailAutomationStep> output)
    {
        var position = 0;
        foreach (var node in nodes)
        {
            Enum.TryParse<EmailAutomationStepType>(node.Type, ignoreCase: true, out var stepType);

            var step = new EmailAutomationStep
            {
                AutomationId = automationId,
                ParentStepId = parentStepId,
                Branch = branch,
                StepType = stepType,
                StepConfigJson = node.Config.ValueKind == JsonValueKind.Undefined ? "{}" : node.Config.GetRawText(),
                Position = position++,
            };
            output.Add(step);

            if (stepType == EmailAutomationStepType.Condition)
            {
                // Children reference this step by Id, assigned only after SaveChanges — setting
                // ParentStep (not ParentStepId) lets EF Core's change-tracker navigation fixup
                // resolve the self-referencing FK once both are saved in the same batch.
                if (node.Yes is not null)
                {
                    var yesChildren = new List<EmailAutomationStep>();
                    FlattenTree(node.Yes, automationId, null, "Yes", yesChildren);
                    foreach (var child in yesChildren)
                    {
                        // yesChildren also holds grandchildren pulled in by a nested Condition's own
                        // recursive call (shares this same buffer as its output) — those already have
                        // ParentStep set to their own immediate Condition ancestor, so only backfill
                        // it for this branch's direct children (still null here).
                        child.ParentStep ??= step;
                    }

                    output.AddRange(yesChildren);
                }

                if (node.No is not null)
                {
                    var noChildren = new List<EmailAutomationStep>();
                    FlattenTree(node.No, automationId, null, "No", noChildren);
                    foreach (var child in noChildren)
                    {
                        child.ParentStep ??= step;
                    }

                    output.AddRange(noChildren);
                }
            }
        }
    }
}
