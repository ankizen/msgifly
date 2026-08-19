using System.ComponentModel.DataAnnotations;
using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Models.ViewModels;

public class EmailSmtpConnectionFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public EmailSmtpProvider Provider { get; set; } = EmailSmtpProvider.Smtp;

    // --- Smtp ---
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }

    /// <summary>Left blank on edit keeps the existing stored value — see EmailSmtpConnectionsController.Save.</summary>
    public string? Password { get; set; }

    public bool EnableSsl { get; set; } = true;

    // --- Brevo / SendGrid / Mailgun / Postmark ---
    public string? ApiKey { get; set; }

    // --- Mailgun ---
    public string? Domain { get; set; }

    /// <summary>Mailgun: "us" | "eu". AmazonSes: an AWS region code.</summary>
    public string? Region { get; set; }

    // --- AmazonSes ---
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }

    // --- Cloudflare ---
    public string? AccountId { get; set; }

    [Required, EmailAddress]
    public string FromEmail { get; set; } = string.Empty;

    public string? FromName { get; set; }

    public bool IsDefault { get; set; }

    [Range(1, 1000)]
    public int MaxSendsPerMinute { get; set; } = 30;

    public bool IsActive { get; set; } = true;
}
