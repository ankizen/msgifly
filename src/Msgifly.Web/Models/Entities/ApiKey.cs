namespace Msgifly.Web.Models.Entities;

/// <summary>
/// Credential for the public REST API (/api/v1/*) — a machine caller (a script, an
/// n8n/Zapier-style automation, a cron) authenticates with one of these the same way a browser
/// session authenticates a human. Only the SHA-256 hash is stored; the plaintext key is shown
/// to the creator exactly once at creation time and never persisted or shown again.
/// </summary>
public class ApiKey
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Short, non-secret display string, e.g. "msgifly_live_a1b2c3d4" — lets the roster show which key is which without ever resurfacing the secret.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>SHA-256 hex digest of the full plaintext key — the per-request auth lookup key.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Comma-separated scope list, e.g. "messages:send,contacts:read".</summary>
    public string ScopesCsv { get; set; } = string.Empty;

    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Null = active. Revoking sets this instead of deleting, so audit/log references stay intact.</summary>
    public DateTime? RevokedAt { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive => RevokedAt is null && (ExpiresAt is null || ExpiresAt > DateTime.UtcNow);
}
