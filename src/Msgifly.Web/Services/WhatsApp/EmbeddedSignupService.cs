using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Msgifly.Web.Services.Settings;

namespace Msgifly.Web.Services.WhatsApp;

/// <summary>
/// The server side of WhatsApp Embedded Signup: exchanges the short-lived authorization code the
/// frontend gets back from FB.login() for a usable, longer-lived access token. Two Graph API
/// hops — first a plain code-for-token exchange, then a token-for-long-lived-token exchange
/// (~60 days) — using the same code = token approach every OAuth client uses against Facebook
/// Login. We store the resulting long-lived user token directly on the Workspace, the same field
/// a manually-pasted System User token goes into (see WabaController.ConnectAccount) — the rest
/// of WhatsAppService doesn't care which path a token came from.
///
/// Known limitation: a long-lived Facebook user token expires after ~60 days and has to be
/// refreshed by reconnecting through this same flow. A true non-expiring token requires manually
/// creating a System User in Meta Business Suite and generating its token there — out of scope
/// for this self-serve flow; ConnectAccount's manual-entry path already covers that case for
/// anyone who sets one up themselves.
/// </summary>
public class EmbeddedSignupService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<EmbeddedSignupService> _logger;

    public EmbeddedSignupService(IHttpClientFactory httpClientFactory, ISettingsService settingsService, ILogger<EmbeddedSignupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<WhatsAppResult<string>> ExchangeCodeForLongLivedTokenAsync(string code)
    {
        var metaApp = await _settingsService.GetAsync<MetaAppSettings>(nameof(MetaAppSettings));
        if (string.IsNullOrWhiteSpace(metaApp.FacebookAppId) || string.IsNullOrWhiteSpace(metaApp.FacebookAppSecret))
        {
            return WhatsAppResult<string>.Fail("Register your Meta App ID/Secret first (step 1 above).");
        }

        var client = _httpClientFactory.CreateClient("GraphApi");
        client.BaseAddress = new Uri($"https://graph.facebook.com/{metaApp.ApiVersion}/");

        var shortLivedResult = await GetTokenAsync(client,
            $"oauth/access_token?client_id={Uri.EscapeDataString(metaApp.FacebookAppId)}" +
            $"&client_secret={Uri.EscapeDataString(metaApp.FacebookAppSecret)}" +
            $"&code={Uri.EscapeDataString(code)}");
        if (!shortLivedResult.Success)
        {
            return shortLivedResult;
        }

        var longLivedResult = await GetTokenAsync(client,
            $"oauth/access_token?grant_type=fb_exchange_token" +
            $"&client_id={Uri.EscapeDataString(metaApp.FacebookAppId)}" +
            $"&client_secret={Uri.EscapeDataString(metaApp.FacebookAppSecret)}" +
            $"&fb_exchange_token={Uri.EscapeDataString(shortLivedResult.Data!)}");

        return longLivedResult;
    }

    private async Task<WhatsAppResult<string>> GetTokenAsync(HttpClient client, string path)
    {
        try
        {
            var response = await client.GetAsync(path);
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            if (!response.IsSuccessStatusCode)
            {
                var message = body?["error"]?["message"]?.GetValue<string>() ?? $"Graph API returned {(int)response.StatusCode}.";
                return WhatsAppResult<string>.Fail(message);
            }

            var token = body?["access_token"]?.GetValue<string>();
            return string.IsNullOrEmpty(token)
                ? WhatsAppResult<string>.Fail("Meta accepted the request but returned no access token.")
                : WhatsAppResult<string>.Ok(token);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Embedded Signup token exchange failed for {Path}", path);
            return WhatsAppResult<string>.Fail("Could not reach Meta to exchange the authorization code.");
        }
    }
}
