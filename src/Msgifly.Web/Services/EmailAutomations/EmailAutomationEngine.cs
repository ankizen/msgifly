using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.Automations;
using Msgifly.Web.Services.Email;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Services.EmailAutomations;

/// <summary>
/// Runs no-code email automations against EmailSubscriber identity only — an independent copy of
/// AutomationEngine's tree-walk-plus-Hangfire-Wait design (deliberately not shared code; the user
/// asked for Email Marketing to stay fully separate from the WhatsApp automation stack). A Wait
/// step suspends the walk and schedules a Hangfire job to resume it later, same as the WhatsApp
/// engine — Hangfire's own SQL Server-backed job storage is what makes that durable, no separate
/// polling table needed here either.
///
/// The only intentional reuse is WebhookUrlGuard (Services.Automations) — a generic SSRF-safety
/// check already shared across unrelated features in this codebase (also used by
/// TrackingDomainVerificationService), not automation business logic.
/// </summary>
public class EmailAutomationEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);

    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly EmailMergeTagRenderer _mergeTagRenderer;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly ILogger<EmailAutomationEngine> _logger;

    public EmailAutomationEngine(
        ApplicationDbContext db,
        IEmailSender emailSender,
        EmailMergeTagRenderer mergeTagRenderer,
        IBackgroundJobClient backgroundJobClient,
        IHttpClientFactory httpClientFactory,
        ICurrentWorkspaceAccessor workspaceAccessor,
        ILogger<EmailAutomationEngine> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _mergeTagRenderer = mergeTagRenderer;
        _backgroundJobClient = backgroundJobClient;
        _httpClientFactory = httpClientFactory;
        _workspaceAccessor = workspaceAccessor;
        _logger = logger;
    }

    public async Task RunForTriggerAsync(EmailAutomationTriggerType triggerType, int subscriberId, EmailAutomationContext context)
    {
        try
        {
            var candidates = await _db.EmailAutomations
                .Where(a => a.TriggerType == triggerType && a.IsActive)
                .ToListAsync();

            foreach (var automation in candidates)
            {
                if (!TriggerMatches(automation, context))
                {
                    continue;
                }

                try
                {
                    await RunAndLogAsync(automation, subscriberId, context);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EmailAutomation {AutomationId} failed to execute", automation.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EmailAutomation dispatch failed for trigger {Trigger}", triggerType);
        }
    }

    /// <summary>Invoked by a scheduled Hangfire job after a Wait step's delay elapses — runs with
    /// no HttpContext, so the workspace accessor is bootstrapped from the automation row first.</summary>
    public async Task ResumeWaitAsync(int automationId, int subscriberId, string contextJson, int? parentStepId, string? branch, int nextPosition, int? logId)
    {
        var automation = await _db.EmailAutomations.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == automationId);
        if (automation is null)
        {
            return;
        }

        _workspaceAccessor.WorkspaceId = automation.WorkspaceId;

        var context = JsonSerializer.Deserialize<EmailAutomationContext>(contextJson, JsonOptions) ?? new EmailAutomationContext();

        try
        {
            var (status, errorMessage) = await ExecuteStepsFromAsync(automation, subscriberId, context, parentStepId, branch, nextPosition, logId);
            if (logId is not null)
            {
                await FinalizeLogAsync(logId.Value, status, errorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EmailAutomation {AutomationId} resume failed", automationId);
            if (logId is not null)
            {
                await FinalizeLogAsync(logId.Value, EmailAutomationLogStatus.Failed, ex.Message);
            }
        }
    }

    /// <summary>Runs one specific automation immediately against a subscriber, bypassing trigger
    /// matching — the future "Test automation" action, mirroring RunAutomationForTestAsync.</summary>
    public async Task<(EmailAutomationLogStatus Status, string? ErrorMessage)> RunAutomationForTestAsync(int automationId, int subscriberId)
    {
        var automation = await _db.EmailAutomations.FirstOrDefaultAsync(a => a.Id == automationId);
        if (automation is null)
        {
            return (EmailAutomationLogStatus.Failed, "Automation not found.");
        }

        return await RunAndLogAsync(automation, subscriberId, new EmailAutomationContext());
    }

    private async Task<(EmailAutomationLogStatus Status, string? ErrorMessage)> RunAndLogAsync(EmailAutomation automation, int subscriberId, EmailAutomationContext context)
    {
        var log = new EmailAutomationLog
        {
            AutomationId = automation.Id,
            SubscriberId = subscriberId,
            ResultJson = "[]",
            // Seeded pessimistically, same reasoning as AutomationLog — only flipped to Success
            // once execution actually reaches the end.
            Status = EmailAutomationLogStatus.Failed,
        };
        _db.EmailAutomationLogs.Add(log);
        await _db.SaveChangesAsync();

        var (status, errorMessage) = await ExecuteStepsFromAsync(automation, subscriberId, context, null, null, 0, log.Id);
        await FinalizeLogAsync(log.Id, status, errorMessage);
        return (status, errorMessage);
    }

    private async Task<(EmailAutomationLogStatus Status, string? ErrorMessage)> ExecuteStepsFromAsync(
        EmailAutomation automation, int subscriberId, EmailAutomationContext context, int? parentStepId, string? branch, int startPosition, int? logId = null)
    {
        var siblings = await _db.EmailAutomationSteps
            .Where(s => s.AutomationId == automation.Id && s.Position >= startPosition)
            .Where(s => parentStepId == null ? s.ParentStepId == null : s.ParentStepId == parentStepId && s.Branch == branch)
            .OrderBy(s => s.Position)
            .ToListAsync();

        if (siblings.Count == 0)
        {
            return (EmailAutomationLogStatus.Success, null);
        }

        EmailAutomationLogStatus? branchDegradation = null;
        string? branchErrorMessage = null;

        foreach (var step in siblings)
        {
            if (step.StepType == EmailAutomationStepType.Stop)
            {
                await AppendLogResultAsync(logId, new EmailAutomationStepResult(step.Id, "Stop", "success", "stopped"));
                return (EmailAutomationLogStatus.Success, null);
            }

            if (step.StepType == EmailAutomationStepType.Wait)
            {
                var cfg = Deserialize<WaitStepConfig>(step.StepConfigJson) ?? new WaitStepConfig();
                var delay = WaitDelay(cfg);
                _backgroundJobClient.Schedule<EmailAutomationEngine>(
                    engine => engine.ResumeWaitAsync(automation.Id, subscriberId, JsonSerializer.Serialize(context, JsonOptions), parentStepId, branch, step.Position + 1, logId),
                    delay);
                await AppendLogResultAsync(logId, new EmailAutomationStepResult(step.Id, "Wait", "success", $"waiting {cfg.Amount} {cfg.Unit}"));
                return (EmailAutomationLogStatus.Partial, null);
            }

            if (step.StepType == EmailAutomationStepType.Condition)
            {
                var cfg = Deserialize<EmailConditionStepConfig>(step.StepConfigJson) ?? new EmailConditionStepConfig();
                bool taken;
                try
                {
                    taken = await EvaluateConditionAsync(cfg, subscriberId);
                }
                catch (Exception ex)
                {
                    await AppendLogResultAsync(logId, new EmailAutomationStepResult(step.Id, "Condition", "failed", ex.Message));
                    return (EmailAutomationLogStatus.Failed, ex.Message);
                }

                await AppendLogResultAsync(logId, new EmailAutomationStepResult(step.Id, "Condition", "success", $"branch={(taken ? "Yes" : "No")}"));

                var (branchStatus, branchError) = await ExecuteStepsFromAsync(automation, subscriberId, context, step.Id, taken ? "Yes" : "No", 0, logId);
                if (branchStatus != EmailAutomationLogStatus.Success && (branchDegradation is null || branchStatus == EmailAutomationLogStatus.Failed))
                {
                    branchDegradation = branchStatus;
                    branchErrorMessage = branchError;
                }

                continue;
            }

            try
            {
                var detail = await RunStepAsync(step, subscriberId);
                await AppendLogResultAsync(logId, new EmailAutomationStepResult(step.Id, step.StepType.ToString(), "success", detail));
            }
            catch (Exception ex)
            {
                await AppendLogResultAsync(logId, new EmailAutomationStepResult(step.Id, step.StepType.ToString(), "failed", ex.Message));
                return (EmailAutomationLogStatus.Failed, ex.Message);
            }
        }

        return branchDegradation is null ? (EmailAutomationLogStatus.Success, null) : (branchDegradation.Value, branchErrorMessage);
    }

    private async Task<string> RunStepAsync(EmailAutomationStep step, int subscriberId)
    {
        var subscriber = await _db.EmailSubscribers.FirstOrDefaultAsync(s => s.Id == subscriberId)
            ?? throw new InvalidOperationException("Subscriber not found.");

        switch (step.StepType)
        {
            case EmailAutomationStepType.SendEmail:
            {
                var cfg = Deserialize<SendEmailStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing SendEmail config.");
                var subject = _mergeTagRenderer.Render(cfg.Subject, subscriber);
                var body = _mergeTagRenderer.Render(cfg.BodyHtml, subscriber);
                var result = await _emailSender.SendAsync(new EmailSendRequest(subscriber.Email, subject, body, Source: $"Automation:{step.AutomationId}"));
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorMessage);
                }

                return "sent";
            }

            case EmailAutomationStepType.AddTag:
            {
                var cfg = Deserialize<TagStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing AddTag config.");
                var exists = await _db.EmailSubscriberTags.AnyAsync(t => t.SubscriberId == subscriberId && t.TagId == cfg.TagId);
                if (!exists)
                {
                    _db.EmailSubscriberTags.Add(new EmailSubscriberTag { SubscriberId = subscriberId, TagId = cfg.TagId });
                    await _db.SaveChangesAsync();
                }

                return $"tag {cfg.TagId} applied";
            }

            case EmailAutomationStepType.RemoveTag:
            {
                var cfg = Deserialize<TagStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing RemoveTag config.");
                var link = await _db.EmailSubscriberTags.FirstOrDefaultAsync(t => t.SubscriberId == subscriberId && t.TagId == cfg.TagId);
                if (link is not null)
                {
                    _db.EmailSubscriberTags.Remove(link);
                    await _db.SaveChangesAsync();
                }

                return $"tag {cfg.TagId} removed";
            }

            case EmailAutomationStepType.AddToList:
            {
                var cfg = Deserialize<ListStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing AddToList config.");
                var exists = await _db.EmailSubscriberLists.AnyAsync(l => l.SubscriberId == subscriberId && l.ListId == cfg.ListId);
                if (!exists)
                {
                    _db.EmailSubscriberLists.Add(new EmailSubscriberList { SubscriberId = subscriberId, ListId = cfg.ListId });
                    await _db.SaveChangesAsync();
                }

                return $"list {cfg.ListId} applied";
            }

            case EmailAutomationStepType.RemoveFromList:
            {
                var cfg = Deserialize<ListStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing RemoveFromList config.");
                var link = await _db.EmailSubscriberLists.FirstOrDefaultAsync(l => l.SubscriberId == subscriberId && l.ListId == cfg.ListId);
                if (link is not null)
                {
                    _db.EmailSubscriberLists.Remove(link);
                    await _db.SaveChangesAsync();
                }

                return $"list {cfg.ListId} removed";
            }

            case EmailAutomationStepType.UpdateSubscriberField:
            {
                var cfg = Deserialize<UpdateSubscriberFieldStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing UpdateSubscriberField config.");
                var value = _mergeTagRenderer.Render(cfg.Value, subscriber);
                var applied = ApplySubscriberField(subscriber, cfg.Field, value);
                if (!applied)
                {
                    return $"field {cfg.Field} not writable from automations";
                }

                subscriber.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return $"{cfg.Field} updated";
            }

            case EmailAutomationStepType.Webhook:
            {
                var cfg = Deserialize<WebhookStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing Webhook config.");
                if (string.IsNullOrWhiteSpace(cfg.Url))
                {
                    throw new InvalidOperationException("webhook needs a url.");
                }

                if (!await WebhookUrlGuard.IsDeliverableAsync(cfg.Url))
                {
                    throw new InvalidOperationException("webhook: destination not allowed.");
                }

                var body = string.IsNullOrEmpty(cfg.BodyTemplate)
                    ? JsonSerializer.Serialize(new { subscriberId, subscriber.Email }, JsonOptions)
                    : _mergeTagRenderer.Render(cfg.BodyTemplate, subscriber);

                using var request = new HttpRequestMessage(HttpMethod.Post, cfg.Url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                if (cfg.Headers is not null)
                {
                    foreach (var (key, value) in cfg.Headers)
                    {
                        request.Headers.TryAddWithoutValidation(key, value);
                    }
                }

                var client = _httpClientFactory.CreateClient("EmailAutomationWebhook");
                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"webhook returned {(int)response.StatusCode}");
                }

                return $"webhook {(int)response.StatusCode}";
            }

            default:
                return $"unknown step: {step.StepType}";
        }
    }

    private static bool ApplySubscriberField(EmailSubscriber subscriber, string field, string value)
    {
        switch (field)
        {
            case "FirstName": subscriber.FirstName = value; return true;
            case "LastName": subscriber.LastName = value; return true;
            case "Phone": subscriber.Phone = value; return true;
            case "Type":
                if (Enum.TryParse<ContactType>(value, ignoreCase: true, out var type))
                {
                    subscriber.Type = type;
                    return true;
                }

                return false;
            default: return false;
        }
    }

    private async Task<bool> EvaluateConditionAsync(EmailConditionStepConfig cfg, int subscriberId)
    {
        switch (cfg.Subject)
        {
            case "SubscriberField":
            {
                if (string.IsNullOrEmpty(cfg.Operand))
                {
                    return false;
                }

                var subscriber = await _db.EmailSubscribers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == subscriberId);
                if (subscriber is null)
                {
                    return false;
                }

                var actual = cfg.Operand switch
                {
                    "FirstName" => subscriber.FirstName,
                    "LastName" => subscriber.LastName,
                    "Email" => subscriber.Email,
                    "Phone" => subscriber.Phone,
                    "Type" => subscriber.Type.ToString(),
                    "Status" => subscriber.Status.ToString(),
                    _ => null,
                };
                return string.Equals(actual ?? string.Empty, cfg.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            case "HasTag":
                return int.TryParse(cfg.Operand, out var tagId) &&
                    await _db.EmailSubscriberTags.AnyAsync(t => t.SubscriberId == subscriberId && t.TagId == tagId);

            case "HasList":
                return int.TryParse(cfg.Operand, out var listId) &&
                    await _db.EmailSubscriberLists.AnyAsync(l => l.SubscriberId == subscriberId && l.ListId == listId);

            default:
                return false;
        }
    }

    private static bool TriggerMatches(EmailAutomation automation, EmailAutomationContext context)
    {
        switch (automation.TriggerType)
        {
            // Null ListId configured means "any list" — matches SubscriberAdded (fired when a
            // subscriber is newly created, optionally via a specific list import) and ListApplied
            // (fired any time an existing subscriber is added to a list) alike, same config shape.
            case EmailAutomationTriggerType.SubscriberAdded:
            case EmailAutomationTriggerType.ListApplied:
            {
                var cfg = Deserialize<ListScopedTriggerConfig>(automation.TriggerConfigJson);
                return cfg?.ListId is null || cfg.ListId == context.ListId;
            }

            case EmailAutomationTriggerType.TagApplied:
            {
                var cfg = Deserialize<TagAppliedTriggerConfig>(automation.TriggerConfigJson);
                return cfg?.TagId is null || cfg.TagId == context.TagId;
            }

            default:
                return true;
        }
    }

    private static TimeSpan WaitDelay(WaitStepConfig cfg)
    {
        var unitMinutes = cfg.Unit switch
        {
            "days" => 1440,
            "hours" => 60,
            _ => 1,
        };
        return TimeSpan.FromMinutes(Math.Max(1, cfg.Amount * unitMinutes));
    }

    private static T? Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task AppendLogResultAsync(int? logId, EmailAutomationStepResult result)
    {
        if (logId is null)
        {
            return;
        }

        var log = await _db.EmailAutomationLogs.FirstOrDefaultAsync(l => l.Id == logId);
        if (log is null)
        {
            return;
        }

        var existing = Deserialize<List<EmailAutomationStepResult>>(log.ResultJson) ?? [];
        existing.Add(result);
        log.ResultJson = JsonSerializer.Serialize(existing, JsonOptions);
        await _db.SaveChangesAsync();
    }

    private async Task FinalizeLogAsync(int logId, EmailAutomationLogStatus status, string? errorMessage)
    {
        var log = await _db.EmailAutomationLogs.FirstOrDefaultAsync(l => l.Id == logId);
        if (log is null)
        {
            return;
        }

        log.Status = status;
        await _db.SaveChangesAsync();
    }
}
