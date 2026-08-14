using System.ComponentModel.DataAnnotations;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Services.WhatsApp;

namespace Msgifly.Web.Models.ViewModels;

public class TemplateButtonFormRow
{
    /// <summary>Empty means "unused slot" — filtered out before building the Meta request.</summary>
    public string Type { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? Url { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Example { get; set; }

    /// <summary>URL buttons only — routes this button through the workspace's tracking domain
    /// instead of sending Url literally. See TemplateButtonRequest.TrackClicks.</summary>
    public bool TrackClicks { get; set; }
}

/// <summary>
/// Backs both the "New Template" and "Edit Template" screens. Buttons and sample-value slots
/// are rendered as a fixed-size list (server truncates to what's actually needed based on the
/// submitted body/header text) rather than a JS-managed dynamic array — simpler to model-bind
/// correctly, and Meta's own limits (10 buttons max) make a fixed cap reasonable anyway.
/// </summary>
public class TemplateFormViewModel
{
    public const int MaxButtons = 10;
    public const int MaxBodyVars = 6;

    /// <summary>Set when editing an existing template; null when creating a new one.</summary>
    public int? Id { get; set; }

    [Required]
    [Display(Name = "Template name")]
    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = "MARKETING";
    public string Language { get; set; } = "en_US";

    /// <summary>none | text | image | video | document.</summary>
    public string HeaderType { get; set; } = "none";
    public string? HeaderContent { get; set; }
    public string? HeaderMediaUrl { get; set; }

    [Required]
    [Display(Name = "Body")]
    public string BodyText { get; set; } = string.Empty;

    public string? FooterText { get; set; }

    public List<TemplateButtonFormRow> Buttons { get; set; } = [.. Enumerable.Range(0, MaxButtons).Select(_ => new TemplateButtonFormRow())];

    public List<string?> SampleBody { get; set; } = [.. Enumerable.Range(0, MaxBodyVars).Select(_ => (string?)null)];
    public List<string?> SampleHeader { get; set; } = [null];

    // Display-only context for the Edit screen.
    public string? ExistingStatus { get; set; }
    public string? RejectionReason { get; set; }
    public string? SubmissionError { get; set; }

    public TemplateCreateRequest ToRequest()
    {
        var bodyVarCount = TemplateValidator.ExtractVariableIndices(BodyText).Count;
        var headerVarCount = string.Equals(HeaderType, "text", StringComparison.OrdinalIgnoreCase)
            ? TemplateValidator.ExtractVariableIndices(HeaderContent).Count
            : 0;

        return new TemplateCreateRequest
        {
            Name = Name.Trim(),
            Category = Category,
            Language = Language,
            HeaderType = HeaderType == "none" ? null : HeaderType,
            HeaderContent = HeaderType == "text" ? HeaderContent : null,
            HeaderMediaUrl = HeaderType is "image" or "video" or "document" ? HeaderMediaUrl : null,
            BodyText = BodyText,
            FooterText = string.IsNullOrWhiteSpace(FooterText) ? null : FooterText,
            Buttons = [.. Buttons
                .Where(b => !string.IsNullOrWhiteSpace(b.Type))
                .Select(b => new TemplateButtonRequest
                {
                    Type = b.Type,
                    Text = b.Text ?? string.Empty,
                    Url = b.Url,
                    PhoneNumber = b.PhoneNumber,
                    Example = b.Example,
                    TrackClicks = b.TrackClicks,
                })],
            SampleValues = new TemplateSampleValues
            {
                Body = [.. SampleBody.Take(bodyVarCount).Select(v => v ?? string.Empty)],
                Header = [.. SampleHeader.Take(headerVarCount).Select(v => v ?? string.Empty)],
            },
        };
    }

    public static TemplateFormViewModel FromEntity(WhatsappTemplate template)
    {
        var model = new TemplateFormViewModel
        {
            Id = template.Id,
            Name = template.TemplateName,
            Category = template.Category,
            Language = template.Language,
            HeaderType = template.HeaderFormat?.ToLowerInvariant() ?? "none",
            HeaderContent = template.HeaderText,
            HeaderMediaUrl = template.HeaderMediaUrl,
            BodyText = template.BodyText,
            FooterText = template.FooterText,
            ExistingStatus = template.Status.ToString(),
            RejectionReason = template.RejectionReason,
            SubmissionError = template.SubmissionError,
        };

        if (!string.IsNullOrEmpty(template.ButtonsJson))
        {
            try
            {
                var buttons = System.Text.Json.JsonSerializer.Deserialize<List<TemplateButtonRequest>>(template.ButtonsJson) ?? [];
                for (var i = 0; i < buttons.Count && i < MaxButtons; i++)
                {
                    model.Buttons[i] = new TemplateButtonFormRow
                    {
                        Type = buttons[i].Type,
                        Text = buttons[i].Text,
                        Url = buttons[i].Url,
                        PhoneNumber = buttons[i].PhoneNumber,
                        Example = buttons[i].Example,
                        TrackClicks = buttons[i].TrackClicks,
                    };
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Rows synced from Meta store Graph API's own button JSON shape (PascalCase keys
                // differ), not ours — nothing to prefill for those; the user re-enters buttons
                // once, on first edit.
            }
        }

        if (!string.IsNullOrEmpty(template.SampleValuesJson))
        {
            try
            {
                var samples = System.Text.Json.JsonSerializer.Deserialize<TemplateSampleValues>(template.SampleValuesJson);
                if (samples is not null)
                {
                    for (var i = 0; i < samples.Body.Count && i < MaxBodyVars; i++)
                    {
                        model.SampleBody[i] = samples.Body[i];
                    }

                    for (var i = 0; i < samples.Header.Count && i < model.SampleHeader.Count; i++)
                    {
                        model.SampleHeader[i] = samples.Header[i];
                    }
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Same story as buttons above — best-effort prefill only.
            }
        }

        return model;
    }
}
