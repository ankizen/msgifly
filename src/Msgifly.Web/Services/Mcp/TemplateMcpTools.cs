using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using Msgifly.Web.Data;
using Msgifly.Web.Services.ApiKeys;
using Msgifly.Web.Services.WhatsApp;

namespace Msgifly.Web.Services.Mcp;

[McpServerToolType]
public class TemplateMcpTools
{
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TemplateMcpTools(ApplicationDbContext db, IWhatsAppService whatsAppService, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _httpContextAccessor = httpContextAccessor;
    }

    [McpServerTool(Name = "list_templates")]
    [Description("Lists this workspace's WhatsApp message templates, with their approval status. A template must be status 'Approved' before it can be used in send_template_message or an automation's SendTemplate step.")]
    public async Task<object> ListTemplatesAsync(
        [Description("Optional filter: Pending, Approved, Rejected, Draft, or Paused. Omit to list all.")] string? status = null)
    {
        _httpContextAccessor.RequireScope(ApiScopes.TemplatesRead);

        var query = _db.WhatsappTemplates.AsNoTracking().OrderByDescending(t => t.Id).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<Models.Enums.TemplateStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(t => t.Status == parsed);
        }

        var templates = await query
            .Select(t => new
            {
                id = t.Id,
                name = t.TemplateName,
                category = t.Category,
                language = t.Language,
                status = t.Status.ToString(),
                headerFormat = t.HeaderFormat,
                bodyText = t.BodyText,
                bodyParamsCount = t.BodyParamsCount,
            })
            .ToListAsync();

        return new { templates };
    }

    [McpServerTool(Name = "create_template")]
    [Description("""
        Creates a new WhatsApp message template and submits it to Meta for approval. It is NOT
        immediately usable — Meta review takes anywhere from minutes to 24 hours; check its status
        with list_templates before referencing it in send_template_message or an automation.

        Category rules (Meta reviews the actual wording, not just the label you pick):
        - MARKETING: any promotional content, new-customer welcomes, offers, "download our app" calls
          to action. Cold outreach to a lead that hasn't messaged you always needs this category.
        - UTILITY: only for messages tied to an existing transaction/account the customer already
          has with you (order updates, appointment confirmations/reminders). Submitting promotional
          content as UTILITY is likely to be rejected by Meta.

        Name must be lowercase a-z, digits, and underscores only (e.g. "salonsteps_lead_welcome").
        Body text may contain {{1}}, {{2}}, ... placeholders; bodySampleValues must supply one
        example string per placeholder, in order, or Meta's review will reject the submission.
        """)]
    public async Task<object> CreateTemplateAsync(
        [Description("Lowercase snake_case name, e.g. salonsteps_lead_welcome")] string name,
        [Description("MARKETING or UTILITY")] string category,
        [Description("BCP-47 language code, e.g. en_US")] string language,
        [Description("Body text, may contain {{1}}, {{2}}, ... placeholders")] string bodyText,
        [Description("One example value per {{n}} placeholder in bodyText, in order. Required if bodyText has any placeholders.")] List<string>? bodySampleValues = null,
        [Description("text | image | video | document, or omit for no header")] string? headerType = null,
        [Description("Header text (for headerType=text; may contain one {{1}}) or a publicly reachable sample media URL (for image/video/document headers)")] string? headerContent = null,
        [Description("Footer text, shown small under the body — no placeholders allowed")] string? footerText = null,
        [Description("""
            Optional buttons as a JSON array, e.g.
            [{"type":"QUICK_REPLY","text":"Yes"},{"type":"URL","text":"Download","url":"https://example.com/{{1}}","example":"app"}]
            Type is QUICK_REPLY, URL, PHONE_NUMBER, or COPY_CODE. Up to 2 URL buttons, up to 10 total.
            """)] string? buttonsJson = null)
    {
        _httpContextAccessor.RequireScope(ApiScopes.TemplatesWrite);

        var request = new TemplateCreateRequest
        {
            Name = name,
            Category = category,
            Language = language,
            BodyText = bodyText,
            FooterText = footerText,
            SampleValues = new TemplateSampleValues { Body = bodySampleValues ?? [] },
        };

        if (!string.IsNullOrWhiteSpace(headerType))
        {
            request.HeaderType = headerType.ToLowerInvariant();
            if (request.HeaderType == "text")
            {
                request.HeaderContent = headerContent;
            }
            else
            {
                request.HeaderMediaUrl = headerContent;
            }
        }

        if (!string.IsNullOrWhiteSpace(buttonsJson))
        {
            try
            {
                request.Buttons = JsonSerializer.Deserialize<List<TemplateButtonRequest>>(buttonsJson, new JsonSerializerOptions(JsonSerializerOptions.Web)) ?? [];
            }
            catch (JsonException ex)
            {
                return new { success = false, error = $"buttonsJson is not valid JSON: {ex.Message}" };
            }
        }

        try
        {
            var result = await _whatsAppService.CreateTemplateAsync(request);
            if (!result.Success)
            {
                return new { success = false, error = result.ErrorMessage };
            }

            return new
            {
                success = true,
                templateId = result.Data!.Id,
                name = result.Data.TemplateName,
                status = result.Data.Status.ToString(),
                message = "Submitted to Meta for approval. Check list_templates for status before using it.",
            };
        }
        catch (ArgumentException ex)
        {
            return new { success = false, error = ex.Message };
        }
    }
}
