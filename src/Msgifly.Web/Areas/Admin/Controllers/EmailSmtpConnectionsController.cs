using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.Email;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class EmailSmtpConnectionsController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public EmailSmtpConnectionsController(ApplicationDbContext db, IEmailSender emailSender, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _emailSender = emailSender;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "email_smtp.view")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var query = _db.EmailSmtpConnections.AsNoTracking().OrderByDescending(c => c.IsDefault).ThenBy(c => c.Name);
        return View(await PagedList<EmailSmtpConnection>.CreateAsync(query, page, PageSize));
    }

    [Authorize(Policy = "email_smtp.create,email_smtp.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        if (id is null)
        {
            return View(new EmailSmtpConnectionFormViewModel());
        }

        var connection = await _db.EmailSmtpConnections.FindAsync(id.Value);
        if (connection is null)
        {
            return NotFound();
        }

        return View(new EmailSmtpConnectionFormViewModel
        {
            Id = connection.Id,
            Name = connection.Name,
            Provider = connection.Provider,
            Host = connection.Host,
            Port = connection.Port ?? 587,
            Username = connection.Username,
            EnableSsl = connection.EnableSsl,
            Domain = connection.Domain,
            Region = connection.Region,
            FromEmail = connection.FromEmail,
            FromName = connection.FromName,
            IsDefault = connection.IsDefault,
            MaxSendsPerMinute = connection.MaxSendsPerMinute,
            IsActive = connection.IsActive,
            // Secrets (Password/ApiKey/AccessKey/SecretKey) are never re-populated into the form —
            // left blank means "keep what's already stored", same convention as Workspace.AccessToken.
        });
    }

    [HttpPost]
    [Authorize(Policy = "email_smtp.create,email_smtp.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmailSmtpConnectionFormViewModel model)
    {
        ValidateProviderFields(model);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (model.IsDefault)
        {
            // Exactly one default connection per workspace — clear any existing one first.
            var currentDefaults = await _db.EmailSmtpConnections.Where(c => c.IsDefault).ToListAsync();
            foreach (var existing in currentDefaults)
            {
                existing.IsDefault = false;
            }
        }

        EmailSmtpConnection connection;
        bool isNew = model.Id is null;
        if (isNew)
        {
            connection = new EmailSmtpConnection { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value };
            _db.EmailSmtpConnections.Add(connection);
        }
        else
        {
            var existing = await _db.EmailSmtpConnections.FindAsync(model.Id!.Value);
            if (existing is null)
            {
                return NotFound();
            }

            connection = existing;
            connection.UpdatedAt = DateTime.UtcNow;
        }

        connection.Name = model.Name;
        connection.Provider = model.Provider;
        connection.Host = model.Host;
        connection.Port = model.Port;
        connection.Username = model.Username;
        connection.EnableSsl = model.EnableSsl;
        connection.Domain = model.Domain;
        connection.Region = model.Region;
        connection.FromEmail = model.FromEmail;
        connection.FromName = model.FromName;
        connection.IsDefault = model.IsDefault;
        connection.MaxSendsPerMinute = model.MaxSendsPerMinute;
        connection.IsActive = model.IsActive;

        // Secrets only overwritten when the form actually posted a new value — a blank field on
        // edit keeps whatever's already stored, same convention as Workspace.AccessToken.
        if (!string.IsNullOrWhiteSpace(model.Password)) connection.Password = model.Password;
        if (!string.IsNullOrWhiteSpace(model.ApiKey)) connection.ApiKey = model.ApiKey;
        if (!string.IsNullOrWhiteSpace(model.AccessKey)) connection.AccessKey = model.AccessKey;
        if (!string.IsNullOrWhiteSpace(model.SecretKey)) connection.SecretKey = model.SecretKey;

        this.Notify(isNew ? "SMTP connection created." : "SMTP connection updated.");

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "email_smtp.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTest(int id, string toEmail)
    {
        var connection = await _db.EmailSmtpConnections.FindAsync(id);
        if (connection is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(toEmail))
        {
            this.Notify("Enter an email address to send the test to.", "danger");
            return RedirectToAction(nameof(Save), new { id });
        }

        var result = await _emailSender.SendAsync(new EmailSendRequest(
            toEmail.Trim(), "Msgifly test email", "<p>This is a test email from your Msgifly SMTP connection.</p>", connection.FromEmail, connection.FromName, "Transactional"));

        this.Notify(result.Success ? $"Test email sent to {toEmail}." : $"Test email failed: {result.ErrorMessage}", result.Success ? "success" : "danger");
        return RedirectToAction(nameof(Save), new { id });
    }

    [HttpPost]
    [Authorize(Policy = "email_smtp.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var connection = await _db.EmailSmtpConnections.FindAsync(id);
        if (connection is null)
        {
            return NotFound();
        }

        _db.EmailSmtpConnections.Remove(connection);
        await _db.SaveChangesAsync();
        this.Notify("SMTP connection deleted.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Each provider needs a different subset of credential fields — validated here
    /// rather than via static [Required] attributes, since which fields are required depends on
    /// the selected Provider. Secrets are exempt on edit (blank = keep existing) but required on
    /// create, same reasoning as the Password field already had.</summary>
    private void ValidateProviderFields(EmailSmtpConnectionFormViewModel model)
    {
        bool isNew = model.Id is null;

        switch (model.Provider)
        {
            case EmailSmtpProvider.Smtp:
                if (string.IsNullOrWhiteSpace(model.Host))
                {
                    ModelState.AddModelError(nameof(model.Host), "Host is required.");
                }

                if (isNew && string.IsNullOrWhiteSpace(model.Password))
                {
                    ModelState.AddModelError(nameof(model.Password), "Password is required for a new connection.");
                }

                break;

            case EmailSmtpProvider.Brevo:
            case EmailSmtpProvider.SendGrid:
            case EmailSmtpProvider.Postmark:
                if (isNew && string.IsNullOrWhiteSpace(model.ApiKey))
                {
                    ModelState.AddModelError(nameof(model.ApiKey), "API key is required.");
                }

                break;

            case EmailSmtpProvider.Mailgun:
                if (isNew && string.IsNullOrWhiteSpace(model.ApiKey))
                {
                    ModelState.AddModelError(nameof(model.ApiKey), "API key is required.");
                }

                if (string.IsNullOrWhiteSpace(model.Domain))
                {
                    ModelState.AddModelError(nameof(model.Domain), "Domain is required.");
                }

                break;

            case EmailSmtpProvider.AmazonSes:
                if (isNew && string.IsNullOrWhiteSpace(model.AccessKey))
                {
                    ModelState.AddModelError(nameof(model.AccessKey), "Access key is required.");
                }

                if (isNew && string.IsNullOrWhiteSpace(model.SecretKey))
                {
                    ModelState.AddModelError(nameof(model.SecretKey), "Secret key is required.");
                }

                if (string.IsNullOrWhiteSpace(model.Region))
                {
                    ModelState.AddModelError(nameof(model.Region), "Region is required.");
                }

                break;
        }
    }
}
