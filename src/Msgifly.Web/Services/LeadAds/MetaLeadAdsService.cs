using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Msgifly.Web.Services.Settings;

namespace Msgifly.Web.Services.LeadAds;

/// <summary>
/// Thin Graph API wrapper for the Facebook Pages / Lead Ads side of things — separate from
/// WhatsAppService because it authenticates differently (a Facebook user token, then a
/// page-scoped token) and talks to different endpoints (Pages, Lead Forms) than the WhatsApp
/// Cloud API does. Used by LeadAdsController (page connection) and LeadAdsSyncJob (the polling
/// import itself).
/// </summary>
public class MetaLeadAdsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<MetaLeadAdsService> _logger;

    public MetaLeadAdsService(IHttpClientFactory httpClientFactory, ISettingsService settingsService, ILogger<MetaLeadAdsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// FB.login() (lead-ads.js) hands back a short-lived user token — good for roughly 1-2 hours.
    /// A Page access token requested with THAT token inherits the same short lifetime, so it works
    /// right after connecting and then silently stops a couple hours later with no obvious signal
    /// (Meta doesn't push an expiry notification; the next sync attempt just starts 401ing). This
    /// exchanges it for a long-lived user token (~60 days) first — a Page token derived from THAT
    /// doesn't expire on its own, which is what the connect flow actually needs.
    /// </summary>
    public async Task<LeadAdsResult<string>> ExchangeForLongLivedTokenAsync(string shortLivedToken)
    {
        try
        {
            var metaApp = await _settingsService.GetAsync<MetaAppSettings>(nameof(MetaAppSettings));
            if (string.IsNullOrWhiteSpace(metaApp.FacebookAppId) || string.IsNullOrWhiteSpace(metaApp.FacebookAppSecret))
            {
                return LeadAdsResult<string>.Fail("Meta App ID/Secret not configured.");
            }

            var client = _httpClientFactory.CreateClient("GraphApi");
            client.BaseAddress = new Uri($"https://graph.facebook.com/{metaApp.ApiVersion}/");
            var query = "oauth/access_token"
                + $"?grant_type=fb_exchange_token&client_id={Uri.EscapeDataString(metaApp.FacebookAppId)}"
                + $"&client_secret={Uri.EscapeDataString(metaApp.FacebookAppSecret)}&fb_exchange_token={Uri.EscapeDataString(shortLivedToken)}";

            var response = await client.GetAsync(query);
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            if (!response.IsSuccessStatusCode)
            {
                var message = body?["error"]?["message"]?.GetValue<string>() ?? $"Graph API returned {(int)response.StatusCode}.";
                return LeadAdsResult<string>.Fail(message);
            }

            var longLivedToken = body?["access_token"]?.GetValue<string>();
            return string.IsNullOrEmpty(longLivedToken)
                ? LeadAdsResult<string>.Fail("Graph API didn't return a long-lived token.")
                : LeadAdsResult<string>.Ok(longLivedToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to exchange the Facebook user token for a long-lived one");
            return LeadAdsResult<string>.Fail("Could not reach the Graph API.");
        }
    }

    public async Task<LeadAdsResult<List<FacebookPageInfo>>> GetUserPagesAsync(string userAccessToken)
    {
        var response = await GetAsync(userAccessToken, "me/accounts?fields=id,name,access_token&limit=200");
        if (!response.Success)
        {
            return LeadAdsResult<List<FacebookPageInfo>>.Fail(response.ErrorMessage!);
        }

        var pages = new List<FacebookPageInfo>();
        foreach (var item in response.Data!["data"]?.AsArray() ?? [])
        {
            if (item is null)
            {
                continue;
            }

            pages.Add(new FacebookPageInfo
            {
                Id = item["id"]?.GetValue<string>() ?? string.Empty,
                Name = item["name"]?.GetValue<string>() ?? string.Empty,
                AccessToken = item["access_token"]?.GetValue<string>() ?? string.Empty,
            });
        }

        return LeadAdsResult<List<FacebookPageInfo>>.Ok(pages);
    }

    public async Task<LeadAdsResult<List<LeadFormInfo>>> GetLeadFormsAsync(string pageId, string pageAccessToken)
    {
        var response = await GetAsync(pageAccessToken, $"{pageId}/leadgen_forms?fields=id,name,status,created_time&limit=200");
        if (!response.Success)
        {
            return LeadAdsResult<List<LeadFormInfo>>.Fail(response.ErrorMessage!);
        }

        var forms = new List<LeadFormInfo>();
        foreach (var item in response.Data!["data"]?.AsArray() ?? [])
        {
            if (item is null)
            {
                continue;
            }

            forms.Add(new LeadFormInfo
            {
                Id = item["id"]?.GetValue<string>() ?? string.Empty,
                Name = item["name"]?.GetValue<string>() ?? string.Empty,
                Status = item["status"]?.GetValue<string>() ?? string.Empty,
                CreatedTime = ParseMetaTimestamp(item["created_time"]),
            });
        }

        return LeadAdsResult<List<LeadFormInfo>>.Ok(forms);
    }

    /// <summary>The form's question schema (key/label/type per question) — fetched once per form
    /// and cached locally (LeadAdsSyncJob.UpsertFormsAsync) since a published form's questions
    /// don't change, and this is what lets a lead's field_data (keyed by arbitrary strings for
    /// CUSTOM questions) get interpreted reliably instead of guessed at by key name.</summary>
    public async Task<LeadAdsResult<List<LeadFormQuestion>>> GetFormQuestionsAsync(string formId, string pageAccessToken)
    {
        var response = await GetAsync(pageAccessToken, $"{formId}?fields=questions{{key,label,type}}");
        if (!response.Success)
        {
            return LeadAdsResult<List<LeadFormQuestion>>.Fail(response.ErrorMessage!);
        }

        var questions = new List<LeadFormQuestion>();
        foreach (var item in response.Data!["questions"]?.AsArray() ?? [])
        {
            if (item is null)
            {
                continue;
            }

            questions.Add(new LeadFormQuestion
            {
                Key = item["key"]?.GetValue<string>() ?? string.Empty,
                Label = item["label"]?.GetValue<string>() ?? string.Empty,
                Type = item["type"]?.GetValue<string>() ?? string.Empty,
            });
        }

        return LeadAdsResult<List<LeadFormQuestion>>.Ok(questions);
    }

    /// <summary>Most-recent-first, capped at `limit` — LeadAdsSyncJob dedupes against LeadAdsImport rather than asking Meta to filter by time, so a modest fixed page size is enough to always cover what's arrived since the last poll.</summary>
    public async Task<LeadAdsResult<List<LeadInfo>>> GetRecentLeadsAsync(string formId, string pageAccessToken, int limit = 50)
    {
        var response = await GetAsync(pageAccessToken, $"{formId}/leads?fields=id,created_time,field_data&limit={limit}");
        if (!response.Success)
        {
            return LeadAdsResult<List<LeadInfo>>.Fail(response.ErrorMessage!);
        }

        var leads = new List<LeadInfo>();
        foreach (var item in response.Data!["data"]?.AsArray() ?? [])
        {
            if (item is null)
            {
                continue;
            }

            leads.Add(ParseLead(item));
        }

        return LeadAdsResult<List<LeadInfo>>.Ok(leads);
    }

    /// <summary>Fetches exactly one lead by id — what the leadgen webhook path uses, since Meta's
    /// push notification only carries the leadgen_id itself, not the answers (same "notify then
    /// fetch" shape as the WhatsApp status webhooks already handle).</summary>
    public async Task<LeadAdsResult<LeadInfo>> GetLeadByIdAsync(string leadgenId, string pageAccessToken)
    {
        var response = await GetAsync(pageAccessToken, $"{leadgenId}?fields=id,created_time,field_data");
        if (!response.Success)
        {
            return LeadAdsResult<LeadInfo>.Fail(response.ErrorMessage!);
        }

        return LeadAdsResult<LeadInfo>.Ok(ParseLead(response.Data!));
    }

    /// <summary>Turns on realtime leadgen push notifications for this specific Page — a one-time
    /// call made right after connecting it (mirrors WhatsAppService.SubscribeWebhookAsync for the
    /// WABA side). Requires the Meta App's own Webhooks product to already have the "page" object
    /// + "leadgen" field enabled in the App Dashboard against the same callback URL used for
    /// WhatsApp — that part can't be driven from our side via API, only this per-Page opt-in can.</summary>
    public async Task<LeadAdsResult> SubscribePageWebhookAsync(string pageId, string pageAccessToken)
    {
        try
        {
            var metaApp = await _settingsService.GetAsync<MetaAppSettings>(nameof(MetaAppSettings));
            var client = _httpClientFactory.CreateClient("GraphApi");
            client.BaseAddress = new Uri($"https://graph.facebook.com/{metaApp.ApiVersion}/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pageAccessToken);

            var response = await client.PostAsync($"{pageId}/subscribed_apps?subscribed_fields=leadgen", content: null);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonObject>();
                var message = body?["error"]?["message"]?.GetValue<string>() ?? $"Graph API returned {(int)response.StatusCode}.";
                return LeadAdsResult.Fail(message);
            }

            return LeadAdsResult.Ok();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe Page {PageId} to the leadgen webhook", pageId);
            return LeadAdsResult.Fail("Could not reach the Graph API.");
        }
    }

    private static LeadInfo ParseLead(JsonNode item)
    {
        var lead = new LeadInfo
        {
            Id = item["id"]?.GetValue<string>() ?? string.Empty,
            CreatedTime = ParseMetaTimestamp(item["created_time"]) ?? DateTime.UtcNow,
        };

        foreach (var field in item["field_data"]?.AsArray() ?? [])
        {
            var name = field?["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            lead.Fields[name] = field!["values"]?.AsArray().Select(v => v?.GetValue<string>() ?? string.Empty).ToList() ?? [];
        }

        return lead;
    }

    private async Task<LeadAdsResult<JsonObject>> GetAsync(string accessToken, string path)
    {
        try
        {
            var metaApp = await _settingsService.GetAsync<MetaAppSettings>(nameof(MetaAppSettings));
            var client = _httpClientFactory.CreateClient("GraphApi");
            client.BaseAddress = new Uri($"https://graph.facebook.com/{metaApp.ApiVersion}/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync(path);
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            if (!response.IsSuccessStatusCode)
            {
                var message = body?["error"]?["message"]?.GetValue<string>() ?? $"Graph API returned {(int)response.StatusCode}.";
                return LeadAdsResult<JsonObject>.Fail(message);
            }

            return LeadAdsResult<JsonObject>.Ok(body ?? new JsonObject());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Lead Ads Graph API request failed for {Path}", path);
            return LeadAdsResult<JsonObject>.Fail("Could not reach the Graph API.");
        }
    }

    /// <summary>Meta returns timestamps as ISO 8601 strings (e.g. "2026-08-01T10:00:00+0000") —
    /// JsonNode.GetValue&lt;DateTime&gt;() only works for numeric/native-DateTime nodes and throws
    /// on a string node, so this always reads it as a string first and parses that.</summary>
    private static DateTime? ParseMetaTimestamp(JsonNode? node)
    {
        var raw = node?.GetValue<string>();
        return DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
