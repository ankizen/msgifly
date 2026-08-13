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
                CreatedTime = item["created_time"]?.GetValue<DateTime>(),
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

            var lead = new LeadInfo
            {
                Id = item["id"]?.GetValue<string>() ?? string.Empty,
                CreatedTime = item["created_time"]?.GetValue<DateTime>() ?? DateTime.UtcNow,
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

            leads.Add(lead);
        }

        return LeadAdsResult<List<LeadInfo>>.Ok(leads);
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
}
