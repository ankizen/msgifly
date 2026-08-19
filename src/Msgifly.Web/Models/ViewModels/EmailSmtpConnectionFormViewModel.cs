using System.ComponentModel.DataAnnotations;

namespace Msgifly.Web.Models.ViewModels;

public class EmailSmtpConnectionFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    public string Username { get; set; } = string.Empty;

    /// <summary>Left blank on edit keeps the existing stored password — see EmailSmtpConnectionsController.Save.</summary>
    public string? Password { get; set; }

    public bool EnableSsl { get; set; } = true;

    [Required, EmailAddress]
    public string FromEmail { get; set; } = string.Empty;

    public string? FromName { get; set; }

    public bool IsDefault { get; set; }

    [Range(1, 1000)]
    public int MaxSendsPerMinute { get; set; } = 30;

    public bool IsActive { get; set; } = true;
}
