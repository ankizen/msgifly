using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Services.WhatsApp;

/// <summary>
/// Thin wrapper around Meta's WhatsApp Cloud API (Graph API), the equivalent of the original's
/// App\Traits\WhatsApp (master doc §5.1/§5.2) — credential resolution from settings rather than
/// .env, template sync, webhook subscribe, and message sending all live here.
/// </summary>
public class WhatsAppService : IWhatsAppService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Settings.ISettingsService _settingsService;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(
        IHttpClientFactory httpClientFactory,
        Settings.ISettingsService settingsService,
        ApplicationDbContext db,
        ICurrentWorkspaceAccessor workspaceAccessor,
        ILogger<WhatsAppService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsService = settingsService;
        _db = db;
        _workspaceAccessor = workspaceAccessor;
        _logger = logger;
    }

    public async Task<WhatsAppResult> RegisterPhoneNumberAsync(string phoneNumberId, string pin)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult.Fail("WhatsApp Business Account is not configured yet.");
        }

        var client = CreateClient(settings);
        var response = await client.PostAsJsonAsync($"{phoneNumberId}/register", new { messaging_product = "whatsapp", pin });
        if (!response.IsSuccessStatusCode)
        {
            return WhatsAppResult.Fail(await ExtractErrorAsync(response));
        }

        return WhatsAppResult.Ok();
    }

    public async Task<WhatsAppResult<List<PhoneNumberInfo>>> GetPhoneNumbersAsync()
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BusinessAccountId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult<List<PhoneNumberInfo>>.Fail("WhatsApp Business Account is not configured yet.");
        }

        var response = await GetAsync(settings, $"{settings.BusinessAccountId}/phone_numbers");
        if (!response.Success)
        {
            return WhatsAppResult<List<PhoneNumberInfo>>.Fail(response.ErrorMessage!);
        }

        var numbers = new List<PhoneNumberInfo>();
        var data = response.Data!["data"]?.AsArray();
        if (data is not null)
        {
            foreach (var item in data)
            {
                if (item is null)
                {
                    continue;
                }

                numbers.Add(new PhoneNumberInfo
                {
                    Id = item["id"]?.GetValue<string>() ?? string.Empty,
                    DisplayPhoneNumber = item["display_phone_number"]?.GetValue<string>() ?? string.Empty,
                    VerifiedName = item["verified_name"]?.GetValue<string>(),
                    QualityRating = item["quality_rating"]?.GetValue<string>(),
                });
            }
        }

        return WhatsAppResult<List<PhoneNumberInfo>>.Ok(numbers);
    }

    public async Task<WhatsAppResult<BusinessProfileInfo>> GetBusinessProfileAsync(string phoneNumberId)
    {
        var settings = await GetSettingsAsync();
        var response = await GetAsync(settings, $"{phoneNumberId}/whatsapp_business_profile?fields=about,address,description,profile_picture_url,email,websites,vertical");
        if (!response.Success)
        {
            return WhatsAppResult<BusinessProfileInfo>.Fail(response.ErrorMessage!);
        }

        var profile = response.Data!["data"]?.AsArray()?.FirstOrDefault();
        var websites = profile?["websites"]?.AsArray()?.Select(w => w?.GetValue<string>()).Where(w => !string.IsNullOrEmpty(w)).ToList() ?? [];
        var info = new BusinessProfileInfo
        {
            About = profile?["about"]?.GetValue<string>(),
            ProfilePictureUrl = profile?["profile_picture_url"]?.GetValue<string>(),
            Email = profile?["email"]?.GetValue<string>(),
            Address = profile?["address"]?.GetValue<string>(),
            Description = profile?["description"]?.GetValue<string>(),
            Vertical = profile?["vertical"]?.GetValue<string>(),
            Website = websites.ElementAtOrDefault(0),
            Website2 = websites.ElementAtOrDefault(1),
        };

        return WhatsAppResult<BusinessProfileInfo>.Ok(info);
    }

    public async Task<WhatsAppResult<int>> SyncTemplatesAsync()
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BusinessAccountId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult<int>.Fail("WhatsApp Business Account is not configured yet.");
        }

        var response = await GetAsync(settings, $"{settings.BusinessAccountId}/message_templates?fields=id,name,language,status,category,components,rejected_reason&limit=200");
        if (!response.Success)
        {
            return WhatsAppResult<int>.Fail(response.ErrorMessage!);
        }

        var apiTemplateIds = new HashSet<string>();
        var templatesArray = response.Data!["data"]?.AsArray();

        if (templatesArray is not null)
        {
            foreach (var node in templatesArray)
            {
                if (node is null)
                {
                    continue;
                }

                var metaId = node["id"]?.GetValue<string>();
                if (string.IsNullOrEmpty(metaId))
                {
                    continue;
                }

                apiTemplateIds.Add(metaId);
                var parsed = ParseTemplateNode(node, metaId);

                var existing = await _db.WhatsappTemplates.FirstOrDefaultAsync(t => t.MetaTemplateId == metaId);
                if (existing is null)
                {
                    parsed.WorkspaceId = _workspaceAccessor.WorkspaceId!.Value;
                    _db.WhatsappTemplates.Add(parsed);
                }
                else
                {
                    parsed.Id = existing.Id;
                    // ParseTemplateNode never sets WorkspaceId — it can't change on sync, and
                    // SetValues below would otherwise stamp the existing row's real value to 0.
                    parsed.WorkspaceId = existing.WorkspaceId;
                    _db.Entry(existing).CurrentValues.SetValues(parsed);
                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        // Delete local templates no longer present on Meta's side (mirrors the original's diff-and-delete).
        // Locally-created DRAFTs (never submitted, MetaTemplateId is null) are never swept here —
        // they simply aren't Meta's concern yet.
        var toDelete = await _db.WhatsappTemplates
            .Where(t => t.MetaTemplateId != null && !apiTemplateIds.Contains(t.MetaTemplateId))
            .ToListAsync();
        _db.WhatsappTemplates.RemoveRange(toDelete);

        await _db.SaveChangesAsync();

        return WhatsAppResult<int>.Ok(apiTemplateIds.Count);
    }

    public async Task<WhatsAppResult<WhatsappTemplate>> CreateTemplateAsync(TemplateCreateRequest request)
    {
        var validation = TemplateValidator.Validate(request);

        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BusinessAccountId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult<WhatsappTemplate>.Fail("WhatsApp Business Account is not configured yet.");
        }

        var componentsResult = await BuildTemplateComponentsAsync(settings, request);
        if (!componentsResult.Success)
        {
            return WhatsAppResult<WhatsappTemplate>.Fail(componentsResult.ErrorMessage!);
        }

        var metaPayload = new
        {
            name = request.Name,
            category = request.Category.ToUpperInvariant(),
            language = request.Language,
            components = componentsResult.Data,
        };

        var client = CreateClient(settings);
        var response = await client.PostAsJsonAsync($"{settings.BusinessAccountId}/message_templates", metaPayload);
        if (!response.IsSuccessStatusCode)
        {
            return WhatsAppResult<WhatsappTemplate>.Fail(await ExtractErrorAsync(response));
        }

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var metaId = body?["id"]?.GetValue<string>();
        var metaStatus = body?["status"]?.GetValue<string>() ?? "PENDING";
        if (string.IsNullOrEmpty(metaId))
        {
            return WhatsAppResult<WhatsappTemplate>.Fail("Meta accepted the template but returned no id.");
        }

        var entity = MapRequestToEntity(request, validation, new WhatsappTemplate());
        entity.WorkspaceId = _workspaceAccessor.WorkspaceId!.Value;
        entity.MetaTemplateId = metaId;
        entity.Status = metaStatus.Equals("APPROVED", StringComparison.OrdinalIgnoreCase) ? TemplateStatus.Approved : TemplateStatus.Pending;
        _db.WhatsappTemplates.Add(entity);
        await _db.SaveChangesAsync();

        return WhatsAppResult<WhatsappTemplate>.Ok(entity);
    }

    public async Task<WhatsAppResult<WhatsappTemplate>> EditTemplateAsync(int localTemplateId, TemplateCreateRequest request)
    {
        var existing = await _db.WhatsappTemplates.FirstOrDefaultAsync(t => t.Id == localTemplateId);
        if (existing is null)
        {
            return WhatsAppResult<WhatsappTemplate>.Fail("Template not found.");
        }

        if (string.IsNullOrEmpty(existing.MetaTemplateId))
        {
            return WhatsAppResult<WhatsappTemplate>.Fail("This template was never submitted to Meta — use New Template to submit it instead.");
        }

        if (existing.Status is not (TemplateStatus.Approved or TemplateStatus.Rejected or TemplateStatus.Paused))
        {
            return WhatsAppResult<WhatsappTemplate>.Fail($"Templates in status {existing.Status} cannot be edited. Allowed: Approved, Rejected, Paused.");
        }

        var validation = TemplateValidator.Validate(request);

        var settings = await GetSettingsAsync();
        var componentsResult = await BuildTemplateComponentsAsync(settings, request);
        if (!componentsResult.Success)
        {
            return WhatsAppResult<WhatsappTemplate>.Fail(componentsResult.ErrorMessage!);
        }

        var editPayload = new
        {
            category = request.Category.ToUpperInvariant(),
            components = componentsResult.Data,
        };

        var client = CreateClient(settings);
        var response = await client.PostAsJsonAsync(existing.MetaTemplateId, editPayload);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ExtractErrorAsync(response);
            existing.SubmissionError = error;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return WhatsAppResult<WhatsappTemplate>.Fail(error);
        }

        // Meta replaces components wholesale on edit and always bumps status back to PENDING for re-review.
        MapRequestToEntity(request, validation, existing);
        existing.Status = TemplateStatus.Pending;
        existing.SubmissionError = null;
        existing.RejectionReason = null;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return WhatsAppResult<WhatsappTemplate>.Ok(existing);
    }

    public async Task<WhatsAppResult> DeleteTemplateAsync(int localTemplateId)
    {
        var existing = await _db.WhatsappTemplates.FirstOrDefaultAsync(t => t.Id == localTemplateId);
        if (existing is null)
        {
            return WhatsAppResult.Fail("Template not found.");
        }

        if (!string.IsNullOrEmpty(existing.MetaTemplateId))
        {
            var settings = await GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.BusinessAccountId))
            {
                return WhatsAppResult.Fail("WhatsApp Business Account is not configured — cannot delete on Meta.");
            }

            var client = CreateClient(settings);
            var query = $"name={Uri.EscapeDataString(existing.TemplateName)}&hsm_id={Uri.EscapeDataString(existing.MetaTemplateId)}";
            var response = await client.DeleteAsync($"{settings.BusinessAccountId}/message_templates?{query}");
            // A 404 means it's already gone on Meta's side — still proceed to drop the local row.
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                return WhatsAppResult.Fail(await ExtractErrorAsync(response));
            }
        }

        _db.WhatsappTemplates.Remove(existing);
        await _db.SaveChangesAsync();
        return WhatsAppResult.Ok();
    }

    /// <summary>Translates our local request shape into Meta's `components` array (HEADER -> BODY -> FOOTER -> BUTTONS order), including the `example` blocks Meta requires alongside any {{N}} variable.
    /// A media (image/video/document) header can't reference a plain external URL directly, unlike a
    /// live message send — template creation specifically requires a `header_handle` obtained from
    /// Meta's separate Resumable Upload API (App-scoped, not WABA-scoped), so this downloads the
    /// media from the given URL and re-uploads it to Meta first. Async (and no longer static)
    /// because of that upload round-trip.</summary>
    private async Task<WhatsAppResult<List<object>>> BuildTemplateComponentsAsync(ResolvedWhatsAppSettings settings, TemplateCreateRequest request)
    {
        var components = new List<object>();

        if (!string.IsNullOrEmpty(request.HeaderType))
        {
            if (request.HeaderType.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                var header = new Dictionary<string, object?> { ["type"] = "HEADER", ["format"] = "TEXT", ["text"] = request.HeaderContent };
                if (request.SampleValues.Header.Count > 0)
                {
                    header["example"] = new { header_text = request.SampleValues.Header };
                }

                components.Add(header);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.HeaderMediaUrl))
                {
                    return WhatsAppResult<List<object>>.Fail("A header media URL is required for image/video/document headers.");
                }

                var handleResult = await UploadTemplateHeaderHandleAsync(settings, request.HeaderMediaUrl);
                if (!handleResult.Success)
                {
                    return WhatsAppResult<List<object>>.Fail($"Couldn't upload header media to Meta: {handleResult.ErrorMessage}");
                }

                var format = request.HeaderType.ToUpperInvariant();
                components.Add(new
                {
                    type = "HEADER",
                    format,
                    example = new { header_handle = new[] { handleResult.Data } },
                });
            }
        }

        var body = new Dictionary<string, object?> { ["type"] = "BODY", ["text"] = request.BodyText };
        if (request.SampleValues.Body.Count > 0)
        {
            // Meta expects body_text as a 2D array: outer is "examples", inner is the per-variable values.
            body["example"] = new { body_text = new[] { request.SampleValues.Body } };
        }

        components.Add(body);

        if (!string.IsNullOrWhiteSpace(request.FooterText))
        {
            components.Add(new { type = "FOOTER", text = request.FooterText });
        }

        if (request.Buttons.Count > 0)
        {
            components.Add(new
            {
                type = "BUTTONS",
                buttons = request.Buttons.Select(object (b) => b.Type switch
                {
                    "URL" => new { type = "URL", text = b.Text, url = b.Url, example = b.Example is null ? null : new[] { b.Example } },
                    "PHONE_NUMBER" => new { type = "PHONE_NUMBER", text = b.Text, phone_number = b.PhoneNumber },
                    "COPY_CODE" => new { type = "COPY_CODE", text = b.Text, example = new[] { b.Example } },
                    _ => new { type = "QUICK_REPLY", text = b.Text },
                }).ToArray(),
            });
        }

        return WhatsAppResult<List<object>>.Ok(components);
    }

    /// <summary>
    /// Meta's Resumable Upload API — the only way to get a `header_handle` for a template's media
    /// header example. Two calls: start a session scoped to the Facebook App (not the WABA or a
    /// phone number, unlike every other upload in this file), then POST the raw bytes to that
    /// session. The byte-upload step specifically requires the literal "OAuth" auth scheme, not
    /// "Bearer" — confirmed against Meta's own docs, not a typo.
    /// </summary>
    private async Task<WhatsAppResult<string>> UploadTemplateHeaderHandleAsync(ResolvedWhatsAppSettings settings, string mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(settings.FacebookAppId))
        {
            return WhatsAppResult<string>.Fail("Facebook App ID is not configured (Setup → Msgifly Settings).");
        }

        byte[] bytes;
        string mimeType;
        try
        {
            var downloadClient = _httpClientFactory.CreateClient("GraphApi");
            var downloadResponse = await downloadClient.GetAsync(mediaUrl);
            if (!downloadResponse.IsSuccessStatusCode)
            {
                return WhatsAppResult<string>.Fail($"Couldn't download the header media from {mediaUrl} (HTTP {(int)downloadResponse.StatusCode}).");
            }

            bytes = await downloadResponse.Content.ReadAsByteArrayAsync();
            mimeType = downloadResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        }
        catch (HttpRequestException ex)
        {
            return WhatsAppResult<string>.Fail($"Couldn't reach the header media URL: {ex.Message}");
        }

        var client = CreateClient(settings);
        var fileName = Path.GetFileName(new Uri(mediaUrl).LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "header-media";
        }

        var startQuery = $"file_name={Uri.EscapeDataString(fileName)}&file_length={bytes.Length}&file_type={Uri.EscapeDataString(mimeType)}";
        var startResponse = await client.PostAsync($"{settings.FacebookAppId}/uploads?{startQuery}", content: null);
        if (!startResponse.IsSuccessStatusCode)
        {
            return WhatsAppResult<string>.Fail(await ExtractErrorAsync(startResponse));
        }

        var startBody = await startResponse.Content.ReadFromJsonAsync<JsonObject>();
        var sessionId = startBody?["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(sessionId))
        {
            return WhatsAppResult<string>.Fail("Meta didn't return an upload session id.");
        }

        // sessionId looks like "upload:AbC123..." — .NET's Uri class treats the colon as a scheme
        // separator no matter how it's constructed: passed as a bare relative string it's
        // misread as an absolute URI with scheme "upload" (NotSupportedException); explicitly
        // forcing UriKind.Relative doesn't help either, since the constructor rejects a string
        // that *looks* absolute regardless of the requested kind (UriFormatException). The only
        // reliable fix is to skip Uri parsing for this one request and build the full absolute
        // URL as a plain string instead, so "upload:" ends up mid-path rather than string-initial.
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, $"{client.BaseAddress}{sessionId}")
        {
            Content = new ByteArrayContent(bytes),
        };
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("OAuth", settings.AccessToken);
        uploadRequest.Headers.Add("file_offset", "0");
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        var uploadResponse = await client.SendAsync(uploadRequest);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            return WhatsAppResult<string>.Fail(await ExtractErrorAsync(uploadResponse));
        }

        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<JsonObject>();
        var handle = uploadBody?["h"]?.GetValue<string>();
        return string.IsNullOrEmpty(handle)
            ? WhatsAppResult<string>.Fail("Upload succeeded but Meta returned no handle.")
            : WhatsAppResult<string>.Ok(handle);
    }

    private static WhatsappTemplate MapRequestToEntity(TemplateCreateRequest request, TemplateValidator.ValidationResult validation, WhatsappTemplate entity)
    {
        entity.TemplateName = request.Name;
        entity.Category = request.Category.ToUpperInvariant();
        entity.Language = request.Language;
        entity.HeaderFormat = request.HeaderType?.ToUpperInvariant();
        entity.HeaderText = request.HeaderType?.Equals("text", StringComparison.OrdinalIgnoreCase) == true ? request.HeaderContent : null;
        entity.HeaderMediaUrl = request.HeaderType is not null && !request.HeaderType.Equals("text", StringComparison.OrdinalIgnoreCase) ? request.HeaderMediaUrl : null;
        entity.HeaderParamsCount = validation.HeaderVarCount;
        entity.BodyText = request.BodyText;
        entity.BodyParamsCount = validation.BodyVarCount;
        entity.FooterText = request.FooterText;
        entity.FooterParamsCount = 0;
        entity.ButtonsJson = request.Buttons.Count > 0 ? JsonSerializer.Serialize(request.Buttons) : null;
        entity.SampleValuesJson = JsonSerializer.Serialize(request.SampleValues);
        entity.UpdatedAt = DateTime.UtcNow;
        return entity;
    }

    public async Task<WhatsAppResult> SubscribeWebhookAsync()
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BusinessAccountId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult.Fail("WhatsApp Business Account is not configured yet.");
        }

        var client = CreateClient(settings);
        var response = await client.PostAsync($"{settings.BusinessAccountId}/subscribed_apps", content: null);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ExtractErrorAsync(response);
            return WhatsAppResult.Fail(error);
        }

        return WhatsAppResult.Ok();
    }

    public async Task<WhatsAppResult> SendTestMessageAsync(string toPhoneNumber, string messageText)
    {
        var result = await SendPlainTextMessageAsync(toPhoneNumber, messageText);
        return result.Success ? WhatsAppResult.Ok() : WhatsAppResult.Fail(result.ErrorMessage!);
    }

    public async Task<WhatsAppResult<string>> SendPlainTextMessageAsync(string toPhoneNumber, string messageText)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "text",
            text = new { body = messageText },
        };

        return await PostMessageAsync(payload);
    }

    public async Task<WhatsAppResult<string>> SendTemplateMessageAsync(string toPhoneNumber, TemplateSendRequest request)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.DefaultPhoneNumberId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult<string>.Fail("WhatsApp Business Account is not connected.");
        }

        var components = new List<object>();

        if (!string.IsNullOrEmpty(request.HeaderFormat))
        {
            object? headerParameter = request.HeaderFormat.ToUpperInvariant() switch
            {
                "TEXT" when !string.IsNullOrEmpty(request.HeaderText) =>
                    new { type = "text", text = request.HeaderText },
                "IMAGE" when !string.IsNullOrEmpty(request.HeaderMediaUrl) =>
                    new { type = "image", image = new { link = request.HeaderMediaUrl } },
                "DOCUMENT" when !string.IsNullOrEmpty(request.HeaderMediaUrl) =>
                    new { type = "document", document = new { link = request.HeaderMediaUrl } },
                "VIDEO" when !string.IsNullOrEmpty(request.HeaderMediaUrl) =>
                    new { type = "video", video = new { link = request.HeaderMediaUrl } },
                _ => null,
            };

            if (headerParameter is not null)
            {
                components.Add(new { type = "header", parameters = new[] { headerParameter } });
            }
        }

        if (request.BodyParams.Count > 0)
        {
            components.Add(new
            {
                type = "body",
                parameters = request.BodyParams.Select(p => new { type = "text", text = p }).ToArray(),
            });
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "template",
            template = new
            {
                name = request.TemplateName,
                language = new { code = request.Language },
                components,
            },
        };

        return await PostMessageAsync(payload);
    }

    public async Task<WhatsAppResult<string>> UploadMediaAsync(string phoneNumberId, Stream fileStream, string fileName, string mimeType)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult<string>.Fail("Connect a WhatsApp Business Account first.");
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("whatsapp"), "messaging_product");
        content.Add(new StringContent(mimeType), "type");

        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        content.Add(streamContent, "file", fileName);

        var client = CreateClient(settings);
        var response = await client.PostAsync($"{phoneNumberId}/media", content);
        if (!response.IsSuccessStatusCode)
        {
            return WhatsAppResult<string>.Fail(await ExtractErrorAsync(response));
        }

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var mediaId = body?["id"]?.GetValue<string>();
        return string.IsNullOrEmpty(mediaId)
            ? WhatsAppResult<string>.Fail("Upload succeeded but no media id was returned.")
            : WhatsAppResult<string>.Ok(mediaId);
    }

    public async Task<WhatsAppResult<MediaInfo>> GetMediaInfoAsync(string mediaId)
    {
        var settings = await GetSettingsAsync();
        var response = await GetAsync(settings, mediaId);
        if (!response.Success)
        {
            return WhatsAppResult<MediaInfo>.Fail(response.ErrorMessage!);
        }

        var node = response.Data!;
        return WhatsAppResult<MediaInfo>.Ok(new MediaInfo
        {
            MediaId = node["id"]?.GetValue<string>() ?? mediaId,
            Url = node["url"]?.GetValue<string>() ?? string.Empty,
            MimeType = node["mime_type"]?.GetValue<string>() ?? string.Empty,
            Sha256 = node["sha256"]?.GetValue<string>(),
            FileSizeBytes = node["file_size"]?.GetValue<long>() ?? 0,
        });
    }

    public async Task<WhatsAppResult<byte[]>> DownloadMediaBytesAsync(string mediaUrl)
    {
        var settings = await GetSettingsAsync();
        try
        {
            // mediaUrl is an absolute, short-lived signed CDN link (not relative to the Graph API
            // base address), but it still requires the same Bearer token as any other Graph call.
            using var request = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

            var client = _httpClientFactory.CreateClient("GraphApi");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return WhatsAppResult<byte[]>.Fail(await ExtractErrorAsync(response));
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            return WhatsAppResult<byte[]>.Ok(bytes);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to download WhatsApp media from {Url}", mediaUrl);
            return WhatsAppResult<byte[]>.Fail("Could not download the media file.");
        }
    }

    public async Task<WhatsAppResult> DeleteMediaAsync(string mediaId)
    {
        var settings = await GetSettingsAsync();
        var client = CreateClient(settings);
        var response = await client.DeleteAsync(mediaId);
        if (!response.IsSuccessStatusCode)
        {
            return WhatsAppResult.Fail(await ExtractErrorAsync(response));
        }

        return WhatsAppResult.Ok();
    }

    public async Task<WhatsAppResult<string>> SendMediaMessageAsync(string toPhoneNumber, MediaMessageRequest request)
    {
        var mediaType = request.MediaType.ToLowerInvariant();
        object mediaObject = (request.Link, request.MediaId, mediaType) switch
        {
            (_, _, "document") when request.Link is not null => new { link = request.Link, caption = request.Caption, filename = request.Filename },
            (_, _, "document") when request.MediaId is not null => new { id = request.MediaId, caption = request.Caption, filename = request.Filename },
            (not null, _, _) => new { link = request.Link, caption = request.Caption },
            (_, not null, _) => new { id = request.MediaId, caption = request.Caption },
            _ => throw new ArgumentException("Either Link or MediaId must be provided.", nameof(request)),
        };

        var payload = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = toPhoneNumber,
            ["type"] = mediaType,
            [mediaType] = mediaObject,
        };

        return await PostMessageAsync(payload);
    }

    public async Task<WhatsAppResult<string>> SendLocationMessageAsync(string toPhoneNumber, LocationMessageRequest request)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "location",
            location = new
            {
                latitude = request.Latitude,
                longitude = request.Longitude,
                name = request.Name,
                address = request.Address,
            },
        };

        return await PostMessageAsync(payload);
    }

    public async Task<WhatsAppResult<string>> SendContactMessageAsync(string toPhoneNumber, ContactCardRequest contact)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "contacts",
            contacts = new[]
            {
                new
                {
                    name = new
                    {
                        formatted_name = contact.FormattedName,
                        first_name = contact.FirstName,
                        last_name = contact.LastName,
                    },
                    phones = contact.PhoneNumber is null ? null : new[] { new { phone = contact.PhoneNumber, type = "CELL" } },
                    org = contact.Organization is null ? null : new { company = contact.Organization },
                },
            },
        };

        return await PostMessageAsync(payload);
    }

    public async Task<WhatsAppResult> SendReactionAsync(string toPhoneNumber, string messageId, string emoji)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "reaction",
            reaction = new { message_id = messageId, emoji },
        };

        var result = await PostMessageAsync(payload);
        return result.Success ? WhatsAppResult.Ok() : WhatsAppResult.Fail(result.ErrorMessage!);
    }

    public async Task<WhatsAppResult> MarkMessageAsReadAsync(string messageId)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.DefaultPhoneNumberId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult.Fail("Connect a WhatsApp Business Account and choose a default number first.");
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            status = "read",
            message_id = messageId,
            // Shows "typing…" to the customer for up to 25s (or until the next message) — a
            // free signal that someone's actually looking at the conversation, not just silence.
            typing_indicator = new { type = "text" },
        };

        var client = CreateClient(settings);
        var response = await client.PostAsJsonAsync($"{settings.DefaultPhoneNumberId}/messages", payload);
        if (!response.IsSuccessStatusCode)
        {
            return WhatsAppResult.Fail(await ExtractErrorAsync(response));
        }

        return WhatsAppResult.Ok();
    }

    public async Task<WhatsAppResult<string>> SendInteractiveButtonsMessageAsync(
        string toPhoneNumber, string bodyText, List<InteractiveButton> buttons, string? headerText = null, string? footerText = null)
    {
        if (buttons.Count is 0 or > 3)
        {
            return WhatsAppResult<string>.Fail("Interactive button messages need between 1 and 3 buttons.");
        }

        var interactive = new Dictionary<string, object?>
        {
            ["type"] = "button",
            ["header"] = headerText is null ? null : new { type = "text", text = headerText },
            ["body"] = new { text = bodyText },
            ["footer"] = footerText is null ? null : new { text = footerText },
            ["action"] = new
            {
                buttons = buttons.Select(b => new { type = "reply", reply = new { id = b.Id, title = b.Title } }).ToArray(),
            },
        };

        return await PostMessageAsync(new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "interactive",
            interactive,
        });
    }

    public async Task<WhatsAppResult<string>> SendInteractiveListMessageAsync(
        string toPhoneNumber, string bodyText, string buttonText, List<InteractiveListSection> sections, string? headerText = null, string? footerText = null)
    {
        var interactive = new Dictionary<string, object?>
        {
            ["type"] = "list",
            ["header"] = headerText is null ? null : new { type = "text", text = headerText },
            ["body"] = new { text = bodyText },
            ["footer"] = footerText is null ? null : new { text = footerText },
            ["action"] = new
            {
                button = buttonText,
                sections = sections.Select(s => new
                {
                    title = s.Title,
                    rows = s.Rows.Select(r => new { id = r.Id, title = r.Title, description = r.Description }).ToArray(),
                }).ToArray(),
            },
        };

        return await PostMessageAsync(new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "interactive",
            interactive,
        });
    }

    public async Task<WhatsAppResult<string>> SendInteractiveCtaUrlMessageAsync(
        string toPhoneNumber, string bodyText, string buttonText, string url, string? headerText = null, string? footerText = null)
    {
        var interactive = new Dictionary<string, object?>
        {
            ["type"] = "cta_url",
            ["header"] = headerText is null ? null : new { type = "text", text = headerText },
            ["body"] = new { text = bodyText },
            ["footer"] = footerText is null ? null : new { text = footerText },
            ["action"] = new
            {
                name = "cta_url",
                parameters = new { display_text = buttonText, url },
            },
        };

        return await PostMessageAsync(new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "interactive",
            interactive,
        });
    }

    public async Task<WhatsAppResult> UpdateBusinessProfileAsync(string phoneNumberId, BusinessProfileUpdateRequest request)
    {
        var settings = await GetSettingsAsync();
        var payload = new Dictionary<string, object?> { ["messaging_product"] = "whatsapp" };
        if (request.About is not null)
        {
            payload["about"] = request.About;
        }

        if (request.Email is not null)
        {
            payload["email"] = request.Email;
        }

        if (request.Address is not null)
        {
            payload["address"] = request.Address;
        }

        if (request.Description is not null)
        {
            payload["description"] = request.Description;
        }

        if (request.Website is not null || request.Website2 is not null)
        {
            payload["websites"] = new[] { request.Website, request.Website2 }.Where(w => !string.IsNullOrWhiteSpace(w)).ToArray();
        }

        if (request.Vertical is not null)
        {
            payload["vertical"] = request.Vertical;
        }

        var client = CreateClient(settings);
        var response = await client.PostAsJsonAsync($"{phoneNumberId}/whatsapp_business_profile", payload);
        if (!response.IsSuccessStatusCode)
        {
            return WhatsAppResult.Fail(await ExtractErrorAsync(response));
        }

        return WhatsAppResult.Ok();
    }

    public async Task<WhatsAppResult> UpdateBusinessProfilePictureAsync(string phoneNumberId, string profilePictureHandle)
    {
        var settings = await GetSettingsAsync();
        var payload = new { messaging_product = "whatsapp", profile_picture_handle = profilePictureHandle };

        var client = CreateClient(settings);
        var response = await client.PostAsJsonAsync($"{phoneNumberId}/whatsapp_business_profile", payload);
        if (!response.IsSuccessStatusCode)
        {
            return WhatsAppResult.Fail(await ExtractErrorAsync(response));
        }

        return WhatsAppResult.Ok();
    }

    /// <summary>
    /// A whatsapp_business_profile photo needs a handle from Meta's separate Resumable Upload API
    /// — NOT the same "media id" UploadMediaAsync returns from the WhatsApp-specific /media
    /// endpoint (that id is only valid for message attachments; passing it as
    /// profile_picture_handle fails with a generic "Parameter value is not valid"). This does the
    /// two-step dance: describe the file against the Meta App itself to get an upload session,
    /// then push the bytes to that session (a *different* auth scheme — "OAuth", not "Bearer" —
    /// is part of Meta's documented contract for this one call) to get back the {h} handle.
    /// </summary>
    public async Task<WhatsAppResult<string>> UploadProfilePictureHandleAsync(Stream fileStream, string fileName, long fileLength, string mimeType)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.FacebookAppId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult<string>.Fail("Connect a WhatsApp Business Account first.");
        }

        var client = CreateClient(settings);

        var sessionResponse = await client.PostAsJsonAsync($"{settings.FacebookAppId}/uploads", new
        {
            file_length = fileLength,
            file_name = fileName,
            file_type = mimeType,
        });
        if (!sessionResponse.IsSuccessStatusCode)
        {
            return WhatsAppResult<string>.Fail(await ExtractErrorAsync(sessionResponse));
        }

        var sessionBody = await sessionResponse.Content.ReadFromJsonAsync<JsonObject>();
        var sessionId = sessionBody?["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(sessionId))
        {
            return WhatsAppResult<string>.Fail("Meta didn't return an upload session id.");
        }

        // Built as an absolute URL string (not combined via Uri against BaseAddress) because
        // sessionId itself contains a colon (e.g. "upload:AbC..."), which .NET's relative-URI
        // resolution can misparse as a scheme separator.
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{settings.ApiVersion}/{sessionId}");
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("OAuth", settings.AccessToken);
        uploadRequest.Headers.Add("file_offset", "0");
        uploadRequest.Content = new StreamContent(fileStream);
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var uploadResponse = await client.SendAsync(uploadRequest);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            return WhatsAppResult<string>.Fail(await ExtractErrorAsync(uploadResponse));
        }

        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<JsonObject>();
        var handle = uploadBody?["h"]?.GetValue<string>();
        return string.IsNullOrEmpty(handle)
            ? WhatsAppResult<string>.Fail("Upload succeeded but no file handle was returned.")
            : WhatsAppResult<string>.Ok(handle);
    }

    /// <summary>Shared POST-to-/messages helper — every outbound message type shares the same envelope and same "pull the wamid out of messages[0].id" response shape.</summary>
    private async Task<WhatsAppResult<string>> PostMessageAsync(object payload)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.DefaultPhoneNumberId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult<string>.Fail("Connect a WhatsApp Business Account and choose a default number first.");
        }

        var client = CreateClient(settings);
        var response = await client.PostAsJsonAsync($"{settings.DefaultPhoneNumberId}/messages", payload);
        if (!response.IsSuccessStatusCode)
        {
            return WhatsAppResult<string>.Fail(await ExtractErrorAsync(response));
        }

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var messageId = body?["messages"]?.AsArray()?.FirstOrDefault()?["id"]?.GetValue<string>();
        return WhatsAppResult<string>.Ok(messageId ?? string.Empty);
    }

    public async Task<WhatsAppResult<string>> DebugTokenAsync()
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.AccessToken) || string.IsNullOrWhiteSpace(settings.FacebookAppId)
            || string.IsNullOrWhiteSpace(settings.FacebookAppSecret))
        {
            return WhatsAppResult<string>.Fail("Missing app credentials or access token.");
        }

        var appToken = $"{settings.FacebookAppId}|{settings.FacebookAppSecret}";
        var response = await GetAsync(settings, $"debug_token?input_token={Uri.EscapeDataString(settings.AccessToken)}&access_token={Uri.EscapeDataString(appToken)}");
        if (!response.Success)
        {
            return WhatsAppResult<string>.Fail(response.ErrorMessage!);
        }

        return WhatsAppResult<string>.Ok(response.Data!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static WhatsappTemplate ParseTemplateNode(JsonNode node, string metaId)
    {
        var components = node["components"]?.AsArray();

        string? headerFormat = null, headerText = null, footerText = null, buttonsJson = null;
        var bodyText = string.Empty;
        int headerParams = 0, bodyParams = 0, footerParams = 0;

        if (components is not null)
        {
            foreach (var component in components)
            {
                var type = component?["type"]?.GetValue<string>()?.ToUpperInvariant();
                switch (type)
                {
                    case "HEADER":
                        headerFormat = component!["format"]?.GetValue<string>();
                        headerText = component["text"]?.GetValue<string>();
                        headerParams = CountPlaceholders(headerText);
                        break;
                    case "BODY":
                        bodyText = component!["text"]?.GetValue<string>() ?? string.Empty;
                        bodyParams = CountPlaceholders(bodyText);
                        break;
                    case "FOOTER":
                        footerText = component!["text"]?.GetValue<string>();
                        footerParams = CountPlaceholders(footerText);
                        break;
                    case "BUTTONS":
                        buttonsJson = component!["buttons"]?.ToJsonString();
                        break;
                }
            }
        }

        var statusText = node["status"]?.GetValue<string>()?.ToUpperInvariant();
        var status = statusText switch
        {
            "APPROVED" => TemplateStatus.Approved,
            "REJECTED" => TemplateStatus.Rejected,
            "PAUSED" => TemplateStatus.Paused,
            _ => TemplateStatus.Pending,
        };

        return new WhatsappTemplate
        {
            MetaTemplateId = metaId,
            TemplateName = node["name"]?.GetValue<string>() ?? string.Empty,
            Language = node["language"]?.GetValue<string>() ?? "en_US",
            Status = status,
            Category = node["category"]?.GetValue<string>() ?? string.Empty,
            HeaderFormat = headerFormat,
            HeaderText = headerText,
            HeaderParamsCount = headerParams,
            BodyText = bodyText,
            BodyParamsCount = bodyParams,
            FooterText = footerText,
            FooterParamsCount = footerParams,
            ButtonsJson = buttonsJson,
            RejectionReason = node["rejected_reason"]?.GetValue<string>(),
        };
    }

    private static int CountPlaceholders(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return System.Text.RegularExpressions.Regex.Matches(text, @"\{\{\d+\}\}").Count;
    }

    /// <summary>Merges the current Workspace's WABA connection with the global Meta App identity — see ResolvedWhatsAppSettings.</summary>
    private async Task<ResolvedWhatsAppSettings> GetSettingsAsync()
    {
        var metaApp = await _settingsService.GetAsync<Settings.MetaAppSettings>(nameof(Settings.MetaAppSettings));
        var workspaceId = _workspaceAccessor.WorkspaceId;
        var workspace = workspaceId is null
            ? null
            : await _db.Workspaces.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.Id == workspaceId);

        return new ResolvedWhatsAppSettings
        {
            FacebookAppId = metaApp.FacebookAppId,
            FacebookAppSecret = metaApp.FacebookAppSecret,
            ApiVersion = metaApp.ApiVersion,
            BusinessAccountId = workspace?.BusinessAccountId,
            AccessToken = workspace?.AccessToken,
            DefaultPhoneNumberId = workspace?.DefaultPhoneNumberId,
            DefaultPhoneNumber = workspace?.DefaultPhoneNumber,
        };
    }

    public async Task<WhatsAppResult<ConversationalAutomationInfo>> GetConversationalAutomationAsync(string phoneNumberId)
    {
        var settings = await GetSettingsAsync();
        var response = await GetAsync(settings, $"{phoneNumberId}?fields=conversational_automation");
        if (!response.Success)
        {
            return WhatsAppResult<ConversationalAutomationInfo>.Fail(response.ErrorMessage!);
        }

        var node = response.Data!["conversational_automation"];
        var info = new ConversationalAutomationInfo
        {
            Prompts = node?["prompts"]?.AsArray()
                .Select(p => p?.GetValue<string>())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList() ?? [],
            Commands = node?["commands"]?.AsArray()
                .Where(c => c is not null)
                .Select(c => new CommandInfo
                {
                    CommandName = c!["command_name"]?.GetValue<string>() ?? string.Empty,
                    CommandDescription = c["command_description"]?.GetValue<string>() ?? string.Empty,
                })
                .ToList() ?? [],
        };

        return WhatsAppResult<ConversationalAutomationInfo>.Ok(info);
    }

    public async Task<WhatsAppResult> UpdateConversationalAutomationAsync(string phoneNumberId, List<string> prompts, List<CommandInfo> commands)
    {
        var settings = await GetSettingsAsync();
        var commandsJson = JsonSerializer.Serialize(commands.Select(c => new { command_name = c.CommandName, command_description = c.CommandDescription }));
        var promptsJson = JsonSerializer.Serialize(prompts);

        var client = CreateClient(settings);
        var query = $"{phoneNumberId}/conversational_automation?commands={Uri.EscapeDataString(commandsJson)}&prompts={Uri.EscapeDataString(promptsJson)}";
        var response = await client.PostAsync(query, content: null);
        if (!response.IsSuccessStatusCode)
        {
            return WhatsAppResult.Fail(await ExtractErrorAsync(response));
        }

        return WhatsAppResult.Ok();
    }

    public async Task<WhatsAppResult<List<FlowSummary>>> SyncFlowsAsync()
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BusinessAccountId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult<List<FlowSummary>>.Fail("WhatsApp Business Account is not configured yet.");
        }

        var response = await GetAsync(settings, $"{settings.BusinessAccountId}/flows?fields=id,name,categories,status&limit=200");
        if (!response.Success)
        {
            return WhatsAppResult<List<FlowSummary>>.Fail(response.ErrorMessage!);
        }

        var summaries = new List<FlowSummary>();
        var flowsArray = response.Data!["data"]?.AsArray();
        if (flowsArray is not null)
        {
            foreach (var node in flowsArray)
            {
                if (node is null)
                {
                    continue;
                }

                var metaId = node["id"]?.GetValue<string>();
                if (string.IsNullOrEmpty(metaId))
                {
                    continue;
                }

                summaries.Add(new FlowSummary
                {
                    MetaFlowId = metaId,
                    Name = node["name"]?.GetValue<string>() ?? string.Empty,
                    Categories = node["categories"]?.AsArray().Select(c => c?.GetValue<string>()).Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).ToList() ?? [],
                    Status = node["status"]?.GetValue<string>() ?? "DRAFT",
                });
            }
        }

        foreach (var summary in summaries)
        {
            var existing = await _db.Flows.FirstOrDefaultAsync(f => f.MetaFlowId == summary.MetaFlowId);
            var status = summary.Status.Equals("PUBLISHED", StringComparison.OrdinalIgnoreCase) ? Models.Enums.FlowStatus.Published
                : summary.Status.Equals("DEPRECATED", StringComparison.OrdinalIgnoreCase) ? Models.Enums.FlowStatus.Deprecated
                : Models.Enums.FlowStatus.Draft;

            if (existing is null)
            {
                _db.Flows.Add(new Models.Entities.Flow
                {
                    WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
                    MetaFlowId = summary.MetaFlowId,
                    Name = summary.Name,
                    CategoriesJson = JsonSerializer.Serialize(summary.Categories),
                    Status = status,
                });
            }
            else
            {
                existing.Name = summary.Name;
                existing.CategoriesJson = JsonSerializer.Serialize(summary.Categories);
                existing.Status = status;
                existing.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        return WhatsAppResult<List<FlowSummary>>.Ok(summaries);
    }

    public async Task<WhatsAppResult<string>> CreateFlowAsync(string name, List<string> categories, string flowJson)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BusinessAccountId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult<string>.Fail("WhatsApp Business Account is not configured yet.");
        }

        var client = CreateClient(settings);
        var createResponse = await client.PostAsJsonAsync($"{settings.BusinessAccountId}/flows", new { name, categories });
        if (!createResponse.IsSuccessStatusCode)
        {
            return WhatsAppResult<string>.Fail(await ExtractErrorAsync(createResponse));
        }

        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var metaFlowId = createBody?["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(metaFlowId))
        {
            return WhatsAppResult<string>.Fail("Meta accepted the flow but returned no id.");
        }

        var assetResult = await UploadFlowAssetAsync(client, metaFlowId, flowJson);
        if (!assetResult.Success)
        {
            // The flow shell now exists on Meta even though the JSON upload failed — surface both
            // the id (so it can still be found via Sync) and the asset error.
            return WhatsAppResult<string>.Fail($"Flow created ({metaFlowId}) but the layout upload failed: {assetResult.ErrorMessage}");
        }

        return WhatsAppResult<string>.Ok(metaFlowId);
    }

    public async Task<WhatsAppResult> UpdateFlowJsonAsync(string metaFlowId, string flowJson)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult.Fail("WhatsApp Business Account is not configured yet.");
        }

        var client = CreateClient(settings);
        return await UploadFlowAssetAsync(client, metaFlowId, flowJson);
    }

    private static async Task<WhatsAppResult> UploadFlowAssetAsync(HttpClient client, string metaFlowId, string flowJson)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("FLOW_JSON"), "asset_type");
        content.Add(new StringContent("flow.json"), "name");

        using var jsonContent = new StringContent(flowJson);
        jsonContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(jsonContent, "file", "flow.json");

        var response = await client.PostAsync($"{metaFlowId}/assets", content);
        if (!response.IsSuccessStatusCode)
        {
            return WhatsAppResult.Fail(await ExtractErrorAsync(response));
        }

        return WhatsAppResult.Ok();
    }

    public async Task<WhatsAppResult> PublishFlowAsync(string metaFlowId)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult.Fail("WhatsApp Business Account is not configured yet.");
        }

        var client = CreateClient(settings);
        var response = await client.PostAsync($"{metaFlowId}/publish", content: null);
        if (!response.IsSuccessStatusCode)
        {
            return WhatsAppResult.Fail(await ExtractErrorAsync(response));
        }

        return WhatsAppResult.Ok();
    }

    public async Task<WhatsAppResult> DeleteFlowAsync(string metaFlowId)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult.Fail("WhatsApp Business Account is not configured yet.");
        }

        var client = CreateClient(settings);
        var response = await client.DeleteAsync(metaFlowId);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            return WhatsAppResult.Fail(await ExtractErrorAsync(response));
        }

        return WhatsAppResult.Ok();
    }

    public async Task<WhatsAppResult<string>> SendFlowMessageAsync(
        string toPhoneNumber, string metaFlowId, string flowToken, string bodyText, string ctaText, string firstScreenId, string? headerText = null, string? footerText = null)
    {
        var interactive = new Dictionary<string, object?>
        {
            ["type"] = "flow",
            ["header"] = headerText is null ? null : new { type = "text", text = headerText },
            ["body"] = new { text = bodyText },
            ["footer"] = footerText is null ? null : new { text = footerText },
            ["action"] = new
            {
                name = "flow",
                parameters = new
                {
                    flow_message_version = "3",
                    flow_token = flowToken,
                    flow_id = metaFlowId,
                    flow_cta = ctaText,
                    flow_action = "navigate",
                    flow_action_payload = new { screen = firstScreenId },
                },
            },
        };

        return await PostMessageAsync(new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "interactive",
            interactive,
        });
    }

    public async Task<WhatsAppResult<List<PricingAnalyticsDataPoint>>> GetPricingAnalyticsAsync(DateTime startUtc, DateTime endUtc, string granularity, List<string> dimensions)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BusinessAccountId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult<List<PricingAnalyticsDataPoint>>.Fail("WhatsApp Business Account is not configured yet.");
        }

        var startUnix = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var endUnix = new DateTimeOffset(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var dimensionsLiteral = string.Join(",", dimensions.Select(d => $"\"{d}\""));
        var fieldsExpression = $"pricing_analytics.start({startUnix}).end({endUnix}).granularity({granularity}).dimensions([{dimensionsLiteral}])";

        var response = await GetAsync(settings, $"{settings.BusinessAccountId}?fields={Uri.EscapeDataString(fieldsExpression)}");
        if (!response.Success)
        {
            return WhatsAppResult<List<PricingAnalyticsDataPoint>>.Fail(response.ErrorMessage!);
        }

        var dataPoints = new List<PricingAnalyticsDataPoint>();
        var pointsArray = response.Data!["pricing_analytics"]?["data_points"]?.AsArray();
        if (pointsArray is not null)
        {
            foreach (var node in pointsArray)
            {
                if (node is null)
                {
                    continue;
                }

                var start = node["start"]?.GetValue<long>();
                var end = node["end"]?.GetValue<long>();
                dataPoints.Add(new PricingAnalyticsDataPoint
                {
                    PeriodStart = start is null ? DateTime.MinValue : DateTimeOffset.FromUnixTimeSeconds(start.Value).UtcDateTime,
                    PeriodEnd = end is null ? DateTime.MinValue : DateTimeOffset.FromUnixTimeSeconds(end.Value).UtcDateTime,
                    Volume = node["volume"]?.GetValue<int>() ?? 0,
                    Cost = node["cost"]?.GetValue<decimal>() ?? 0m,
                    PricingCategory = node["pricing_category"]?.GetValue<string>(),
                    PricingType = node["pricing_type"]?.GetValue<string>(),
                });
            }
        }

        return WhatsAppResult<List<PricingAnalyticsDataPoint>>.Ok(dataPoints);
    }

    private HttpClient CreateClient(ResolvedWhatsAppSettings settings)
    {
        var client = _httpClientFactory.CreateClient("GraphApi");
        client.BaseAddress = new Uri($"https://graph.facebook.com/{settings.ApiVersion}/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        return client;
    }

    private async Task<WhatsAppResult<JsonObject>> GetAsync(ResolvedWhatsAppSettings settings, string path)
    {
        try
        {
            var client = CreateClient(settings);
            var response = await client.GetAsync(path);
            if (!response.IsSuccessStatusCode)
            {
                return WhatsAppResult<JsonObject>.Fail(await ExtractErrorAsync(response));
            }

            var json = await response.Content.ReadFromJsonAsync<JsonObject>();
            return WhatsAppResult<JsonObject>.Ok(json ?? new JsonObject());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "WhatsApp Graph API request failed for {Path}", path);
            return WhatsAppResult<JsonObject>.Fail("Could not reach the WhatsApp Cloud API. Check your connection and try again.");
        }
    }

    private static async Task<string> ExtractErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            var message = body?["error"]?["message"]?.GetValue<string>();
            return message ?? $"WhatsApp Cloud API returned {(int)response.StatusCode} {response.StatusCode}.";
        }
        catch
        {
            return $"WhatsApp Cloud API returned {(int)response.StatusCode} {response.StatusCode}.";
        }
    }
}
