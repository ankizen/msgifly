using System.Text.RegularExpressions;
using Msgifly.Web.Models.Entities;

namespace Msgifly.Web.Services.Email;

/// <summary>Substitutes {{subscriber.*}}/{{vars.*}}/{{unsubscribe_link}} tokens — same regex-based
/// style as AutomationEngine.Interpolate, but its own independent copy (no shared code between the
/// two stacks). Reads App:PublicBaseUrl for building the absolute unsubscribe link, since campaign
/// dispatch runs as a Hangfire job with no HttpContext to derive a host from.</summary>
public class EmailMergeTagRenderer
{
    private static readonly Regex TokenPattern = new(@"\{\{\s*([\w.]+)\s*\}\}", RegexOptions.Compiled);

    private readonly string _publicBaseUrl;

    public EmailMergeTagRenderer(IConfiguration configuration)
    {
        _publicBaseUrl = (configuration["App:PublicBaseUrl"] ?? "https://app.msgifly.com").TrimEnd('/');
    }

    public string Render(string text, EmailSubscriber subscriber, string? trackingToken = null, Dictionary<string, string>? vars = null) =>
        TokenPattern.Replace(text, match =>
        {
            var key = match.Groups[1].Value;
            if (key == "unsubscribe_link")
            {
                return trackingToken is null ? string.Empty : $"{_publicBaseUrl}/e/u/{trackingToken}";
            }

            var parts = key.Split('.', 2);
            if (parts.Length == 2 && parts[0] == "vars" && vars is not null && vars.TryGetValue(parts[1], out var v))
            {
                return v;
            }

            if (parts.Length == 2 && parts[0] == "subscriber")
            {
                return parts[1] switch
                {
                    "firstName" => string.IsNullOrWhiteSpace(subscriber.FirstName) ? "there" : subscriber.FirstName,
                    "lastName" => subscriber.LastName ?? string.Empty,
                    "fullName" => string.IsNullOrWhiteSpace(subscriber.FullName) ? "there" : subscriber.FullName,
                    "email" => subscriber.Email,
                    _ => string.Empty,
                };
            }

            return string.Empty;
        });
}
