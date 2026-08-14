using System.Text.RegularExpressions;

namespace Msgifly.Web.Services.WhatsApp;

/// <summary>
/// Pure validators for locally-authored templates, run before submitting to Meta so a
/// misconfigured template fails fast with a specific, field-level message instead of a generic
/// 400 from the Graph API. Limits follow Meta's published Cloud API template rules.
/// </summary>
public static class TemplateValidator
{
    public const int BodyMaxLength = 1024;
    public const int FooterMaxLength = 60;
    public const int HeaderTextMaxLength = 60;
    public const int ButtonTextMaxLength = 25;
    public const int MaxButtonsTotal = 10;
    public const int MaxUrlButtons = 2;
    public const int MaxPhoneButtons = 1;
    public const int MaxCopyCodeButtons = 1;

    private static readonly Regex NameRegex = new(@"^[a-z0-9_]{1,512}$", RegexOptions.Compiled);
    private static readonly Regex VariableRegex = new(@"\{\{(\d+)\}\}", RegexOptions.Compiled);

    public record ValidationResult(int BodyVarCount, int HeaderVarCount);

    /// <summary>Runs every rule in order; throws on the first failure. Returns variable counts for
    /// reuse when building the Meta payload. trackingDomainActive gates whether any button may set
    /// TrackClicks — a template can never look "tracking-enabled" in the UI while actually
    /// submitting the untracked real URL, so this is a hard failure, not a silent fallback.</summary>
    public static ValidationResult Validate(TemplateCreateRequest request, bool trackingDomainActive = false)
    {
        ValidateName(request.Name);
        if (string.IsNullOrWhiteSpace(request.Language))
        {
            throw new ArgumentException("Language is required.");
        }

        if (string.Equals(request.Category, "AUTHENTICATION", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("AUTHENTICATION templates aren't supported here — create them in Meta Business Manager and use Sync from Meta.");
        }

        var bodyVars = ValidateBody(request.BodyText);
        ValidateFooter(request.FooterText);
        var headerVarCount = ValidateHeader(request);
        ValidateButtons(request.Buttons, trackingDomainActive);
        ValidateSampleValues(request, bodyVars.Count, headerVarCount);

        return new ValidationResult(bodyVars.Count, headerVarCount);
    }

    public static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Template name is required.");
        }

        if (!NameRegex.IsMatch(name))
        {
            throw new ArgumentException("Template name must use only lowercase letters, digits, and underscores (1-512 chars).");
        }
    }

    /// <summary>Sorted, deduplicated {{N}} indices, e.g. "Hi {{1}} {{2}}" -> [1, 2].</summary>
    public static List<int> ExtractVariableIndices(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var set = new SortedSet<int>();
        foreach (Match m in VariableRegex.Matches(text))
        {
            if (int.TryParse(m.Groups[1].Value, out var n) && n >= 1)
            {
                set.Add(n);
            }
        }

        return [.. set];
    }

    /// <summary>Meta requires contiguous, 1-indexed variables — {{1}} {{3}} is invalid, must be {{1}} {{2}}.</summary>
    private static void AssertContiguous(List<int> indices, string where)
    {
        for (var i = 0; i < indices.Count; i++)
        {
            if (indices[i] != i + 1)
            {
                throw new ArgumentException($"{where} variables must be contiguous starting at {{{{1}}}} — found {string.Join(", ", indices.Select(n => $"{{{{{n}}}}}"))}.");
            }
        }
    }

    public static List<int> ValidateBody(string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            throw new ArgumentException("Body text is required.");
        }

        if (bodyText.Length > BodyMaxLength)
        {
            throw new ArgumentException($"Body text exceeds {BodyMaxLength} chars (got {bodyText.Length}).");
        }

        var indices = ExtractVariableIndices(bodyText);
        AssertContiguous(indices, "Body");
        return indices;
    }

    public static void ValidateFooter(string? footerText)
    {
        if (string.IsNullOrEmpty(footerText))
        {
            return;
        }

        if (footerText.Length > FooterMaxLength)
        {
            throw new ArgumentException($"Footer text exceeds {FooterMaxLength} chars (got {footerText.Length}).");
        }

        if (ExtractVariableIndices(footerText).Count > 0)
        {
            throw new ArgumentException("Footer text cannot contain {{N}} variables (Meta rule).");
        }
    }

    /// <summary>Returns the header's variable count (0 or 1 — only TEXT headers can have one).</summary>
    public static int ValidateHeader(TemplateCreateRequest request)
    {
        if (string.IsNullOrEmpty(request.HeaderType))
        {
            return 0;
        }

        if (string.Equals(request.HeaderType, "text", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.HeaderContent))
            {
                throw new ArgumentException("Text header requires header content.");
            }

            if (request.HeaderContent.Length > HeaderTextMaxLength)
            {
                throw new ArgumentException($"Header text exceeds {HeaderTextMaxLength} chars (got {request.HeaderContent.Length}).");
            }

            var indices = ExtractVariableIndices(request.HeaderContent);
            if (indices.Count > 1)
            {
                throw new ArgumentException($"Text header supports at most one variable — found {indices.Count} (Meta rule).");
            }

            if (indices.Count == 1 && indices[0] != 1)
            {
                throw new ArgumentException("Text header variable must be {{1}} (Meta rule).");
            }

            return indices.Count;
        }

        // image / video / document headers need a public sample URL.
        if (string.IsNullOrWhiteSpace(request.HeaderMediaUrl))
        {
            throw new ArgumentException($"{request.HeaderType} header requires a public sample URL.");
        }

        if (!Uri.TryCreate(request.HeaderMediaUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new ArgumentException("Header media URL must be a valid http(s) URL.");
        }

        return 0;
    }

    private static Dictionary<string, int> CountButtonsByType(List<TemplateButtonRequest> buttons)
    {
        var counts = new Dictionary<string, int> { ["QUICK_REPLY"] = 0, ["URL"] = 0, ["PHONE_NUMBER"] = 0, ["COPY_CODE"] = 0 };
        foreach (var b in buttons)
        {
            if (counts.ContainsKey(b.Type))
            {
                counts[b.Type]++;
            }
        }

        return counts;
    }

    public static void ValidateButtons(List<TemplateButtonRequest> buttons, bool trackingDomainActive = false)
    {
        if (buttons is null || buttons.Count == 0)
        {
            return;
        }

        if (buttons.Count > MaxButtonsTotal)
        {
            throw new ArgumentException($"Templates can have at most {MaxButtonsTotal} buttons (got {buttons.Count}).");
        }

        var counts = CountButtonsByType(buttons);
        if (counts["URL"] > MaxUrlButtons)
        {
            throw new ArgumentException($"At most {MaxUrlButtons} URL buttons allowed (got {counts["URL"]}).");
        }

        if (counts["PHONE_NUMBER"] > MaxPhoneButtons)
        {
            throw new ArgumentException($"At most {MaxPhoneButtons} PHONE_NUMBER button allowed (got {counts["PHONE_NUMBER"]}).");
        }

        if (counts["COPY_CODE"] > MaxCopyCodeButtons)
        {
            throw new ArgumentException($"At most {MaxCopyCodeButtons} COPY_CODE button allowed (got {counts["COPY_CODE"]}).");
        }

        // QUICK_REPLY buttons must be grouped at the start — Meta rejects interleaving with CTA buttons.
        var sawNonQr = false;
        foreach (var b in buttons)
        {
            if (b.Type == "QUICK_REPLY")
            {
                if (sawNonQr)
                {
                    throw new ArgumentException("QUICK_REPLY buttons cannot be interleaved with URL / PHONE_NUMBER / COPY_CODE buttons — group them at the start.");
                }
            }
            else
            {
                sawNonQr = true;
            }
        }

        for (var i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            if (string.IsNullOrWhiteSpace(b.Text))
            {
                throw new ArgumentException($"Button #{i + 1} ({b.Type}) is missing text.");
            }

            if (b.Text.Length > ButtonTextMaxLength)
            {
                throw new ArgumentException($"Button #{i + 1} text exceeds {ButtonTextMaxLength} chars.");
            }

            switch (b.Type)
            {
                case "URL":
                    if (string.IsNullOrWhiteSpace(b.Url))
                    {
                        throw new ArgumentException($"URL button #{i + 1} is missing a url.");
                    }

                    if (!Uri.TryCreate(b.Url, UriKind.Absolute, out _))
                    {
                        throw new ArgumentException($"URL button #{i + 1} has an invalid url.");
                    }

                    var urlVars = ExtractVariableIndices(b.Url);
                    if (urlVars.Count > 1)
                    {
                        throw new ArgumentException($"URL button #{i + 1} can have at most one variable (Meta rule).");
                    }

                    if (b.TrackClicks)
                    {
                        if (urlVars.Count > 0)
                        {
                            throw new ArgumentException($"URL button #{i + 1} already has its own {{{{1}}}} variable — can't also enable click tracking, which needs that same slot.");
                        }

                        if (!trackingDomainActive)
                        {
                            throw new ArgumentException($"URL button #{i + 1} has click tracking on, but no active tracking domain is configured for this workspace yet — set one up in Workspace Settings first.");
                        }
                    }
                    else if (urlVars.Count == 1)
                    {
                        if (urlVars[0] != 1)
                        {
                            throw new ArgumentException($"URL button #{i + 1} variable must be {{{{1}}}} (Meta rule).");
                        }

                        if (string.IsNullOrWhiteSpace(b.Example))
                        {
                            throw new ArgumentException($"URL button #{i + 1} uses {{{{1}}}} — Meta requires an example value.");
                        }
                    }

                    break;
                case "PHONE_NUMBER":
                    if (string.IsNullOrWhiteSpace(b.PhoneNumber))
                    {
                        throw new ArgumentException($"PHONE_NUMBER button #{i + 1} is missing a phone number.");
                    }

                    break;
                case "COPY_CODE":
                    if (string.IsNullOrWhiteSpace(b.Example))
                    {
                        throw new ArgumentException($"COPY_CODE button #{i + 1} is missing an example value.");
                    }

                    break;
            }
        }
    }

    /// <summary>Sample values must line up 1:1 with the variables found in the header/body text — Meta uses these for human review.</summary>
    public static void ValidateSampleValues(TemplateCreateRequest request, int bodyVarCount, int headerVarCount)
    {
        var body = request.SampleValues.Body ?? [];
        var header = request.SampleValues.Header ?? [];

        if (body.Count != bodyVarCount)
        {
            throw new ArgumentException($"Body has {bodyVarCount} variable(s) — supply exactly {bodyVarCount} sample value(s) (got {body.Count}).");
        }

        if (header.Count != headerVarCount)
        {
            throw new ArgumentException($"Header has {headerVarCount} variable(s) — supply exactly {headerVarCount} sample value(s) (got {header.Count}).");
        }

        for (var i = 0; i < body.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(body[i]))
            {
                throw new ArgumentException($"Body sample value #{i + 1} is empty.");
            }
        }

        for (var i = 0; i < header.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(header[i]))
            {
                throw new ArgumentException($"Header sample value #{i + 1} is empty.");
            }
        }
    }
}
