using System.Text.Json;
using System.Text.Json.Nodes;
using Msgifly.Web.Services.Settings;

namespace Msgifly.Web.Services.Tracking;

/// <summary>
/// Registers a workspace's tracking domain with this app's own Coolify deployment, so it starts
/// routing/getting a cert with no human ever running a manual deploy. A docker-compose app's
/// domains live in Coolify's docker_compose_domains field (JSON-encoded, keyed per compose service)
/// and only take effect on the running container after a redeploy — so "add a domain" here means:
/// fetch the current list, safely append (never drop what's already there), verify the write
/// actually took (Coolify's PATCH has a known intermittent bug where it silently no-ops), then
/// trigger a deploy. Symmetric RemoveDomainAsync exists so clearing a workspace's domain cleans up
/// after itself instead of leaving stale entries.
/// </summary>
public class CoolifyDomainService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<CoolifyDomainService> _logger;

    public CoolifyDomainService(IHttpClientFactory httpClientFactory, ISettingsService settingsService, ILogger<CoolifyDomainService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<(bool Success, string? Error)> AddDomainAsync(string domain)
    {
        var settings = await GetSettingsOrNullAsync();
        if (settings is null)
        {
            return (false, "Coolify integration isn't configured — the domain was saved locally, but couldn't be registered automatically. Contact support.");
        }

        try
        {
            var client = BuildClient(settings);
            var current = await GetCurrentDomainsAsync(client, settings);
            if (current is null)
            {
                return (false, "Couldn't read the current deployment configuration from Coolify.");
            }

            var target = NormalizeToUrl(domain);
            if (current.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                // Already registered (e.g. re-saving the same value) — still worth a deploy in case
                // a prior attempt updated Coolify's record but never got redeployed.
                return await TriggerDeployAsync(client, settings);
            }

            var updated = new List<string>(current) { target };
            return await WriteDomainsAsync(client, settings, current, updated);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Coolify domain registration failed for {Domain}", domain);
            return (false, "Couldn't reach Coolify to register the domain automatically. Try \"Check now\" again shortly.");
        }
    }

    public async Task<(bool Success, string? Error)> RemoveDomainAsync(string domain)
    {
        var settings = await GetSettingsOrNullAsync();
        if (settings is null)
        {
            return (true, null); // nothing to clean up if it was never configured
        }

        try
        {
            var client = BuildClient(settings);
            var current = await GetCurrentDomainsAsync(client, settings);
            if (current is null)
            {
                return (false, "Couldn't read the current deployment configuration from Coolify.");
            }

            var target = NormalizeToUrl(domain);
            if (!current.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                return (true, null); // already not present
            }

            var updated = current.Where(d => !string.Equals(d, target, StringComparison.OrdinalIgnoreCase)).ToList();
            return await WriteDomainsAsync(client, settings, current, updated);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Coolify domain removal failed for {Domain}", domain);
            return (false, "Couldn't reach Coolify to remove the domain automatically.");
        }
    }

    private async Task<CoolifyIntegrationSettings?> GetSettingsOrNullAsync()
    {
        var settings = await _settingsService.GetAsync<CoolifyIntegrationSettings>(nameof(CoolifyIntegrationSettings));
        return string.IsNullOrWhiteSpace(settings.ApiToken) || string.IsNullOrWhiteSpace(settings.ApplicationUuid) || string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? null
            : settings;
    }

    private HttpClient BuildClient(CoolifyIntegrationSettings settings)
    {
        var client = _httpClientFactory.CreateClient("Coolify");
        client.BaseAddress = new Uri(settings.BaseUrl!.TrimEnd('/') + "/api/v1/");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiToken);
        return client;
    }

    private async Task<List<string>?> GetCurrentDomainsAsync(HttpClient client, CoolifyIntegrationSettings settings)
    {
        var response = await client.GetAsync($"applications/{settings.ApplicationUuid}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var raw = body?["docker_compose_domains"]?.GetValue<string?>();
        if (string.IsNullOrEmpty(raw))
        {
            return [];
        }

        var parsed = JsonNode.Parse(raw)?.AsObject();
        var domainCsv = parsed?[settings.ComposeServiceName]?["domain"]?.GetValue<string?>();
        return string.IsNullOrEmpty(domainCsv)
            ? []
            : [.. domainCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private async Task<(bool Success, string? Error)> WriteDomainsAsync(HttpClient client, CoolifyIntegrationSettings settings, List<string> before, List<string> after)
    {
        // Hard safety net: whatever else this computed, the app's own required domains must still
        // be in the final list — never let a bug here take the app itself offline.
        var missingRequired = settings.RequiredDomains.Where(required => !after.Contains(required, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missingRequired.Count > 0)
        {
            _logger.LogError("Refusing to write Coolify domains — required domain(s) {Missing} would be dropped. Before={Before} After={After}",
                string.Join(", ", missingRequired), string.Join(",", before), string.Join(",", after));
            return (false, "Safety check failed — refused to update the domain list. No changes were made.");
        }

        var domainCsv = string.Join(',', after);
        // Coolify's documented schema for this field is an array of {name, domain} objects (one per
        // compose service) — NOT the string-encoded-object shape the GET response happens to return
        // it as. Read and write use different representations of the same data.
        var payload = new
        {
            docker_compose_domains = new object[]
            {
                new { name = settings.ComposeServiceName, domain = domainCsv },
            },
        };

        var patchResponse = await client.PatchAsJsonAsync($"applications/{settings.ApplicationUuid}", payload);
        if (!patchResponse.IsSuccessStatusCode)
        {
            var body = await patchResponse.Content.ReadAsStringAsync();
            _logger.LogWarning("Coolify PATCH rejected ({Status}): {Body}", (int)patchResponse.StatusCode, body);
            return (false, $"Coolify rejected the domain update ({(int)patchResponse.StatusCode}).");
        }

        // Coolify's domain-PATCH has a known intermittent bug where it silently doesn't take —
        // re-read and confirm before trusting it enough to trigger a deploy.
        var verify = await GetCurrentDomainsAsync(client, settings);
        if (verify is null || !after.All(d => verify.Contains(d, StringComparer.OrdinalIgnoreCase)) || verify.Count != after.Count)
        {
            _logger.LogWarning("Coolify domain PATCH didn't take effect. Expected={Expected} Actual={Actual}", string.Join(",", after), string.Join(",", verify ?? []));
            return (false, "Coolify accepted the update but didn't apply it — this is a known intermittent issue on their end. Try \"Check now\" again in a minute.");
        }

        return await TriggerDeployAsync(client, settings);
    }

    private static async Task<(bool Success, string? Error)> TriggerDeployAsync(HttpClient client, CoolifyIntegrationSettings settings)
    {
        var deployResponse = await client.PostAsync($"deploy?uuid={settings.ApplicationUuid}", null);
        return deployResponse.IsSuccessStatusCode
            ? (true, null)
            : (false, $"Domain was registered, but triggering the deploy failed ({(int)deployResponse.StatusCode}). It'll pick up on the next regular deploy.");
    }

    private static string NormalizeToUrl(string domain)
    {
        var trimmed = domain.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }
}
