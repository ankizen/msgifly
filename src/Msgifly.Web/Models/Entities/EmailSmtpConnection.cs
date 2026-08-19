using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.Entities;

/// <summary>Mail sending connection — one physical table covering multiple provider types
/// (mirrors FluentSMTP's Providers/config.php: one shape per provider, irrelevant fields for a
/// given Provider just stay null/empty). Password/ApiKey/SecretKey are plaintext, matching
/// Workspace.AccessToken's existing convention — encryption-at-rest is a known, flagged gap.</summary>
public class EmailSmtpConnection
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;

    public EmailSmtpProvider Provider { get; set; } = EmailSmtpProvider.Smtp;

    // --- Smtp provider ---
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; } = true;

    // --- API-key providers: Brevo, SendGrid, Mailgun, Postmark ---
    public string? ApiKey { get; set; }

    // --- Mailgun only ---
    public string? Domain { get; set; }

    /// <summary>Mailgun: "us" | "eu". AmazonSes: an AWS region code (e.g. "us-east-1").</summary>
    public string? Region { get; set; }

    // --- AmazonSes only ---
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }

    public string FromEmail { get; set; } = string.Empty;
    public string? FromName { get; set; }

    /// <summary>Fallback connection when a send's FromEmail doesn't exact-match any connection.</summary>
    public bool IsDefault { get; set; }

    public int MaxSendsPerMinute { get; set; } = 30;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
