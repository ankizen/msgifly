using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;

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
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(
        IHttpClientFactory httpClientFactory,
        Settings.ISettingsService settingsService,
        ApplicationDbContext db,
        ILogger<WhatsAppService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsService = settingsService;
        _db = db;
        _logger = logger;
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
        var response = await GetAsync(settings, $"{phoneNumberId}/whatsapp_business_profile?fields=about,profile_picture_url,email,websites");
        if (!response.Success)
        {
            return WhatsAppResult<BusinessProfileInfo>.Fail(response.ErrorMessage!);
        }

        var profile = response.Data!["data"]?.AsArray()?.FirstOrDefault();
        var info = new BusinessProfileInfo
        {
            About = profile?["about"]?.GetValue<string>(),
            ProfilePictureUrl = profile?["profile_picture_url"]?.GetValue<string>(),
            Email = profile?["email"]?.GetValue<string>(),
            Websites = profile?["websites"]?.AsArray()?.FirstOrDefault()?.GetValue<string>(),
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

        var response = await GetAsync(settings, $"{settings.BusinessAccountId}/message_templates?fields=id,name,language,status,category,components&limit=200");
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
                    _db.WhatsappTemplates.Add(parsed);
                }
                else
                {
                    parsed.Id = existing.Id;
                    _db.Entry(existing).CurrentValues.SetValues(parsed);
                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        // Delete local templates no longer present on Meta's side (mirrors the original's diff-and-delete).
        var toDelete = await _db.WhatsappTemplates
            .Where(t => !apiTemplateIds.Contains(t.MetaTemplateId))
            .ToListAsync();
        _db.WhatsappTemplates.RemoveRange(toDelete);

        await _db.SaveChangesAsync();

        return WhatsAppResult<int>.Ok(apiTemplateIds.Count);
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

    public Task<WhatsAppResult> SendTestMessageAsync(string toPhoneNumber, string messageText) =>
        SendPlainTextMessageAsync(toPhoneNumber, messageText);

    public async Task<WhatsAppResult> SendPlainTextMessageAsync(string toPhoneNumber, string messageText)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.DefaultPhoneNumberId) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return WhatsAppResult.Fail("Connect a WhatsApp Business Account and choose a default number first.");
        }

        var client = CreateClient(settings);
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "text",
            text = new { body = messageText },
        };

        var response = await client.PostAsJsonAsync($"{settings.DefaultPhoneNumberId}/messages", payload);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ExtractErrorAsync(response);
            return WhatsAppResult.Fail(error);
        }

        return WhatsAppResult.Ok();
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

    private async Task<Settings.WhatsAppSettings> GetSettingsAsync() =>
        await _settingsService.GetAsync<Settings.WhatsAppSettings>(nameof(Settings.WhatsAppSettings));

    private HttpClient CreateClient(Settings.WhatsAppSettings settings)
    {
        var client = _httpClientFactory.CreateClient("GraphApi");
        client.BaseAddress = new Uri($"https://graph.facebook.com/{settings.ApiVersion}/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        return client;
    }

    private async Task<WhatsAppResult<JsonObject>> GetAsync(Settings.WhatsAppSettings settings, string path)
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
