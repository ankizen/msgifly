using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Msgifly.Web.Models.Entities;

namespace Msgifly.Web.Models.ViewModels;

/// <summary>
/// Backs the "New Flow" / "Edit Flow" screen. Flow JSON is hand-authored in a textarea rather than
/// a visual builder — Meta's own Flow Builder in WhatsApp Manager already covers visual authoring.
/// </summary>
public class FlowFormViewModel
{
    /// <summary>Set when editing an existing local row; null when creating a new one.</summary>
    public int? Id { get; set; }

    [Required]
    [Display(Name = "Flow name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Meta's fixed category enum, e.g. LEAD_GENERATION, APPOINTMENT_BOOKING, SURVEY, OTHER.</summary>
    public string Category { get; set; } = "OTHER";

    [Required]
    [Display(Name = "Flow JSON")]
    public string FlowJson { get; set; } = string.Empty;

    // Display-only context for the Edit screen.
    public string? MetaFlowId { get; set; }
    public string? ExistingStatus { get; set; }
    public string? SubmissionError { get; set; }

    public static FlowFormViewModel FromEntity(Flow flow)
    {
        var categories = JsonSerializer.Deserialize<List<string>>(flow.CategoriesJson) ?? [];
        return new FlowFormViewModel
        {
            Id = flow.Id,
            Name = flow.Name,
            Category = categories.ElementAtOrDefault(0) ?? "OTHER",
            FlowJson = flow.FlowJson,
            MetaFlowId = flow.MetaFlowId,
            ExistingStatus = flow.Status.ToString(),
            SubmissionError = flow.SubmissionError,
        };
    }
}
