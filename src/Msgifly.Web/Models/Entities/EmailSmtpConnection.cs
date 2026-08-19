namespace Msgifly.Web.Models.Entities;

/// <summary>Generic SMTP relay credentials (no per-provider API integrations — every provider
/// FluentSMTP special-cases also exposes a standard SMTP endpoint, so one shape reaches all of
/// them via MailKit). Password is plaintext, matching Workspace.AccessToken's existing convention
/// — encryption-at-rest is a known, flagged gap, not Phase 1 scope.</summary>
public class EmailSmtpConnection
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
    public string FromEmail { get; set; } = string.Empty;
    public string? FromName { get; set; }

    /// <summary>Fallback connection when a send's FromEmail doesn't exact-match any connection.</summary>
    public bool IsDefault { get; set; }

    public int MaxSendsPerMinute { get; set; } = 30;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
