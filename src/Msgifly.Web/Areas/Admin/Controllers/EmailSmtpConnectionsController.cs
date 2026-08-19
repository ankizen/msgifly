using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
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
            Host = connection.Host,
            Port = connection.Port,
            Username = connection.Username,
            EnableSsl = connection.EnableSsl,
            FromEmail = connection.FromEmail,
            FromName = connection.FromName,
            IsDefault = connection.IsDefault,
            MaxSendsPerMinute = connection.MaxSendsPerMinute,
            IsActive = connection.IsActive,
        });
    }

    [HttpPost]
    [Authorize(Policy = "email_smtp.create,email_smtp.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmailSmtpConnectionFormViewModel model)
    {
        if (model.Id is null && string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(model.Password), "Password is required for a new connection.");
        }

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

        if (model.Id is null)
        {
            _db.EmailSmtpConnections.Add(new EmailSmtpConnection
            {
                WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
                Name = model.Name,
                Host = model.Host,
                Port = model.Port,
                Username = model.Username,
                Password = model.Password ?? string.Empty,
                EnableSsl = model.EnableSsl,
                FromEmail = model.FromEmail,
                FromName = model.FromName,
                IsDefault = model.IsDefault,
                MaxSendsPerMinute = model.MaxSendsPerMinute,
                IsActive = model.IsActive,
            });
            this.Notify("SMTP connection created.");
        }
        else
        {
            var connection = await _db.EmailSmtpConnections.FindAsync(model.Id.Value);
            if (connection is null)
            {
                return NotFound();
            }

            connection.Name = model.Name;
            connection.Host = model.Host;
            connection.Port = model.Port;
            connection.Username = model.Username;
            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                connection.Password = model.Password;
            }

            connection.EnableSsl = model.EnableSsl;
            connection.FromEmail = model.FromEmail;
            connection.FromName = model.FromName;
            connection.IsDefault = model.IsDefault;
            connection.MaxSendsPerMinute = model.MaxSendsPerMinute;
            connection.IsActive = model.IsActive;
            connection.UpdatedAt = DateTime.UtcNow;
            this.Notify("SMTP connection updated.");
        }

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
}
