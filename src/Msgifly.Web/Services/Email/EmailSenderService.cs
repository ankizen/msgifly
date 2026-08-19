using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.Email.Providers;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Services.Email;

/// <summary>
/// Sends via a workspace's configured EmailSmtpConnection, dispatching to the connection's
/// Provider-specific handler (SMTP relay via MailKit, or a real API integration — Brevo,
/// SendGrid, Mailgun, Amazon SES, Postmark — see Services/Email/Providers/). Connection
/// resolution: exact FromEmail match (active) -> else the workspace's IsDefault connection ->
/// else Fail.
/// </summary>
public class EmailSenderService : IEmailSender
{
    private readonly ApplicationDbContext _db;
    private readonly EmailProviderHandlerFactory _handlerFactory;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly ILogger<EmailSenderService> _logger;

    public EmailSenderService(ApplicationDbContext db, EmailProviderHandlerFactory handlerFactory, ICurrentWorkspaceAccessor workspaceAccessor, ILogger<EmailSenderService> logger)
    {
        _db = db;
        _handlerFactory = handlerFactory;
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
            var handler = _handlerFactory.Resolve(connection.Provider);
            var result = await handler.SendAsync(connection, fromEmail, fromName, request);

            var logId = await WriteLogAsync(workspaceId.Value, request, fromEmail, result.Success ? EmailLogStatus.Sent : EmailLogStatus.Failed, result.ErrorMessage);
            return result.Success ? EmailSendResult.Ok(logId) : EmailSendResult.Fail(result.ErrorMessage ?? "Send failed.", logId);
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
