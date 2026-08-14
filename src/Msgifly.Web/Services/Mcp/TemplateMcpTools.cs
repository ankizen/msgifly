using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Msgifly.Web.Data;
using Msgifly.Web.Services.ApiKeys;
using Msgifly.Web.Services.WhatsApp;

namespace Msgifly.Web.Services.Mcp;

[McpServerToolType]
public class TemplateMcpTools
{
    private const long MaxHeaderImageBytes = 16 * 1024 * 1024; // matches Meta's own header-media cap, same as TemplatesController.UploadHeaderMedia

    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IWebHostEnvironment _environment;

    public TemplateMcpTools(ApplicationDbContext db, IWhatsAppService whatsAppService, IHttpContextAccessor httpContextAccessor, IWebHostEnvironment environment)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _httpContextAccessor = httpContextAccessor;
        _environment = environment;
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
        [Description("Header text (for headerType=text; may contain one {{1}}) or a publicly reachable sample media URL (for image/video/document headers) — for an image you have as raw bytes rather than an existing URL, call upload_template_header_image first and pass its returned url here")] string? headerContent = null,
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

    [McpServerTool(Name = "upload_template_header_image")]
    [Description("""
        Uploads image bytes to this server's own storage and returns a public URL — use that url
        as headerContent when calling create_template with headerType=image, so the header image
        is hosted here rather than depending on some third-party site staying up. Same storage
        location and 16 MB limit as the dashboard's own Templates image-upload field.
        """)]
    public async Task<object> UploadTemplateHeaderImageAsync(
        [Description("Base64-encoded image bytes — no 'data:image/...;base64,' prefix, just the encoded bytes")] string imageBase64,
        [Description("Original filename, used only to pick the file extension (e.g. photo.png). Defaults to .png if omitted or extensionless.")] string? fileName = null)
    {
        _httpContextAccessor.RequireScope(ApiScopes.TemplatesWrite);

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(imageBase64);
        }
        catch (FormatException ex)
        {
            throw new McpException($"imageBase64 is not valid base64: {ex.Message}");
        }

        if (bytes.Length == 0)
        {
            throw new McpException("Image data is empty.");
        }

        if (bytes.Length > MaxHeaderImageBytes)
        {
            throw new McpException($"Image is {bytes.Length / 1024 / 1024} MB — larger than WhatsApp's 16 MB header-media limit.");
        }

        var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "templates");
        Directory.CreateDirectory(uploadsDir);
        var extension = string.IsNullOrWhiteSpace(fileName) ? ".png" : Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".png";
        }

        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(uploadsDir, storedFileName), bytes);

        var request = _httpContextAccessor.HttpContext!.Request;
        var publicUrl = $"{request.Scheme}://{request.Host}/uploads/templates/{storedFileName}";

        return new { success = true, url = publicUrl, sizeBytes = bytes.Length };
    }

    [McpServerTool(Name = "delete_template")]
    [Description("""
        Permanently deletes a template — both the local record and, if it was already submitted,
        on Meta itself. This can't be undone.

        Meta only allows EDITING a template that's Approved, Rejected, or Paused — never Pending.
        There's no edit tool here, so to change a Pending template's content (e.g. to add a header
        image after the fact), delete it and call create_template again with the same name instead
        of waiting for a review verdict first.
        """)]
    public async Task<object> DeleteTemplateAsync(
        [Description("Local template id, from list_templates")] int templateId)
    {
        _httpContextAccessor.RequireScope(ApiScopes.TemplatesWrite);

        var result = await _whatsAppService.DeleteTemplateAsync(templateId);
        if (!result.Success)
        {
            return new { success = false, error = result.ErrorMessage };
        }

        return new { success = true };
    }
}
