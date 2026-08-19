using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Services.Email;

/// <summary>
/// Sends via a workspace's configured EmailSmtpConnection using MailKit (SmtpClient/System.Net.Mail
/// is Microsoft-obsolete). Connection resolution: exact FromEmail match (active) -> else the
/// workspace's IsDefault connection -> else Fail. Generic SMTP only — no per-provider API
/// integrations, since every provider FluentSMTP special-cases also exposes a standard SMTP relay
/// endpoint this already reaches.
/// </summary>
public class EmailSenderService : IEmailSender
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly ILogger<EmailSenderService> _logger;

    public EmailSenderService(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor, ILogger<EmailSenderService> logger)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailSendRequest request)
    {
        var workspaceId = _workspaceAccessor.WorkspaceId;
        if (workspaceId is null)
        {
            return EmailSendResult.Fail("No workspace context.");
        }

        var connection = await ResolveConnectionAsync(workspaceId.Value, request.FromEmail);
        if (connection is null)
        {
            var failMessage = "No SMTP connection configured.";
            var failLogId = await WriteLogAsync(workspaceId.Value, request, request.FromEmail ?? string.Empty, EmailLogStatus.Failed, failMessage);
            return EmailSendResult.Fail(failMessage, failLogId);
        }

        var fromEmail = request.FromEmail ?? connection.FromEmail;
        var fromName = request.FromName ?? connection.FromName;

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName ?? fromEmail, fromEmail));
            message.To.Add(MailboxAddress.Parse(request.ToEmail));
            message.Subject = request.Subject;
            message.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = request.BodyHtml };

            using var client = new SmtpClient();
            await client.ConnectAsync(connection.Host, connection.Port, connection.EnableSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None);
            if (!string.IsNullOrEmpty(connection.Username))
            {
                await client.AuthenticateAsync(connection.Username, connection.Password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            var logId = await WriteLogAsync(workspaceId.Value, request, fromEmail, EmailLogStatus.Sent, null);
            return EmailSendResult.Ok(logId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email send to {ToEmail} failed", request.ToEmail);
            var logId = await WriteLogAsync(workspaceId.Value, request, fromEmail, EmailLogStatus.Failed, ex.Message);
            return EmailSendResult.Fail(ex.Message, logId);
        }
    }

    private async Task<EmailSmtpConnection?> ResolveConnectionAsync(int workspaceId, string? fromEmail)
    {
        var connections = await _db.EmailSmtpConnections.AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId && c.IsActive)
            .ToListAsync();

        if (!string.IsNullOrEmpty(fromEmail))
        {
            var exactMatch = connections.FirstOrDefault(c => string.Equals(c.FromEmail, fromEmail, StringComparison.OrdinalIgnoreCase));
            if (exactMatch is not null)
            {
                return exactMatch;
            }
        }

        return connections.FirstOrDefault(c => c.IsDefault) ?? connections.FirstOrDefault();
    }

    private async Task<int> WriteLogAsync(int workspaceId, EmailSendRequest request, string fromEmail, EmailLogStatus status, string? errorMessage)
    {
        var log = new EmailLog
        {
            WorkspaceId = workspaceId,
            ToEmail = request.ToEmail,
            FromEmail = fromEmail,
            Subject = request.Subject,
            Status = status,
            ResponseMessage = errorMessage,
            Source = request.Source,
            SentAt = status == EmailLogStatus.Sent ? DateTime.UtcNow : null,
        };
        _db.EmailLogs.Add(log);
        await _db.SaveChangesAsync();
        return log.Id;
    }
}
