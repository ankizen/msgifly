using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Msgifly.Web.Data;
using Msgifly.Web.Services.ApiKeys;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Authorization;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions;

/// <summary>
/// Authenticates /api/v1/* requests via `Authorization: Bearer msgifly_live_...`, separate from
/// the cookie scheme the dashboard uses. Registered as an additional scheme (see Program.cs) —
/// the human cookie login is untouched, this only applies where a controller explicitly opts in
/// with [Authorize(AuthenticationSchemes = "ApiKey")].
///
/// Each key belongs to one Workspace, and this handler is the only thing that knows which one
/// until the key itself is matched — so the lookup runs with IgnoreQueryFilters() (nothing else
/// could scope it yet) and then explicitly sets ICurrentWorkspaceAccessor once found, overriding
/// whatever cookie-based default WorkspaceResolutionMiddleware set earlier in the pipeline.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApplicationDbContext db,
        ICurrentWorkspaceAccessor workspaceAccessor)
        : base(options, logger, encoder)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return AuthenticateResult.Fail("Missing Authorization header.");
        }

        var raw = authHeader.ToString();
        if (!raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("Authorization header must be 'Bearer <api key>'.");
        }

        var plaintext = raw["Bearer ".Length..].Trim();
        if (!ApiKeyGenerator.LooksLikeApiKey(plaintext))
        {
            return AuthenticateResult.Fail("Malformed API key.");
        }

        var hash = ApiKeyGenerator.Hash(plaintext);
        var apiKey = await _db.ApiKeys.IgnoreQueryFilters().FirstOrDefaultAsync(k => k.KeyHash == hash);
        if (apiKey is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        if (!apiKey.IsActive)
        {
            return AuthenticateResult.Fail(apiKey.RevokedAt is not null ? "This API key was revoked." : "This API key has expired.");
        }

        _workspaceAccessor.WorkspaceId = apiKey.WorkspaceId;

        apiKey.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, apiKey.Id.ToString()),
            new("api_key_name", apiKey.Name),
        };
        claims.AddRange(apiKey.ScopesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => new Claim("scope", s)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { error = "unauthorized", message = "A valid API key is required." });
    }
}

public static class ApiScopeClaimsExtensions
{
    public static bool HasApiScope(this ClaimsPrincipal user, string scope) => user.HasClaim("scope", scope);

    public static int ApiKeyId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
