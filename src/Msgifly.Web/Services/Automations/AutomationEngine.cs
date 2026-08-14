using System.Text.Json;
using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Hubs;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.WhatsApp;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Services.Automations;

/// <summary>
/// Runs no-code automations: finds active automations matching a fired trigger, then walks each
/// one's step tree (root steps, with Condition steps branching into Yes/No children). A `Wait`
/// step suspends the walk and schedules a Hangfire job to resume it later — no separate
/// "pending executions" polling table needed since Hangfire's own SQL Server-backed job storage
/// already persists scheduled jobs durably.
///
/// Must never let a single automation's failure affect another, or throw out to the webhook
/// handler that dispatched it — callers fire-and-forget. Every per-automation failure is
/// recorded into AutomationLog with Status=Failed instead.
/// </summary>
public class AutomationEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);

    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<AutomationEngine> _logger;

    public AutomationEngine(
        ApplicationDbContext db,
        IWhatsAppService whatsAppService,
        IBackgroundJobClient backgroundJobClient,
        IHttpClientFactory httpClientFactory,
        ICurrentWorkspaceAccessor workspaceAccessor,
        IHubContext<ChatHub> hubContext,
        ILogger<AutomationEngine> logger)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _backgroundJobClient = backgroundJobClient;
        _httpClientFactory = httpClientFactory;
        _workspaceAccessor = workspaceAccessor;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task RunForTriggerAsync(AutomationTriggerType triggerType, int? contactId, AutomationContext context)
    {
        try
        {
            var candidates = await _db.Automations
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
                    await ExecuteAutomationAsync(automation, triggerType, contactId, context);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Automation {AutomationId} failed to execute", automation.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automation dispatch failed for trigger {Trigger}", triggerType);
        }
    }

    /// <summary>
    /// Invoked by a scheduled Hangfire job after a Wait step's delay elapses — runs with no
    /// HttpContext, so the current-workspace accessor is never set going in. Look the automation
    /// up unfiltered first (that's the only way to discover which workspace it belongs to), then
    /// set the accessor before anything else touches a workspace-scoped table.
    /// </summary>
    public async Task ResumeWaitAsync(int automationId, int? contactId, string contextJson, int? parentStepId, string? branch, int nextPosition, int? logId)
    {
        var automation = await _db.Automations.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == automationId);
        if (automation is null)
        {
            return;
        }

        _workspaceAccessor.WorkspaceId = automation.WorkspaceId;

        var context = JsonSerializer.Deserialize<AutomationContext>(contextJson, JsonOptions) ?? new AutomationContext();

        try
        {
            var (status, errorMessage) = await ExecuteStepsFromAsync(automation, contactId, context, parentStepId, branch, nextPosition);
            if (parentStepId is null && logId is not null)
            {
                await FinalizeLogAsync(logId.Value, status, errorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automation {AutomationId} resume failed", automationId);
            if (logId is not null)
            {
                await FinalizeLogAsync(logId.Value, AutomationLogStatus.Failed, ex.Message);
            }
        }
    }

    private async Task ExecuteAutomationAsync(Automation automation, AutomationTriggerType triggerType, int? contactId, AutomationContext context)
    {
        var log = new AutomationLog
        {
            AutomationId = automation.Id,
            ContactId = contactId,
            TriggerEvent = triggerType.ToString(),
            StepsExecutedJson = "[]",
            // Seeded pessimistically — only flipped to Success once execution actually reaches
            // the end. A crash mid-run then correctly reads as Failed rather than a silent,
            // misleading Success with an empty step list.
            Status = AutomationLogStatus.Failed,
        };
        _db.AutomationLogs.Add(log);
        await _db.SaveChangesAsync();

        var (status, errorMessage) = await ExecuteStepsFromAsync(automation, contactId, context, null, null, 0, log.Id);
        await FinalizeLogAsync(log.Id, status, errorMessage);

        automation.ExecutionCount++;
        automation.LastExecutedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    /// <summary>Walks sibling steps in one scope (root, or a Condition's Yes/No branch) starting at startPosition. Returns the terminal status once the scope (or the whole tree, for the root scope) finishes, fails, or suspends on a Wait.</summary>
    private async Task<(AutomationLogStatus Status, string? ErrorMessage)> ExecuteStepsFromAsync(
        Automation automation, int? contactId, AutomationContext context, int? parentStepId, string? branch, int startPosition, int? logId = null)
    {
        var siblings = await _db.AutomationSteps
            .Where(s => s.AutomationId == automation.Id && s.Position >= startPosition)
            .Where(s => parentStepId == null ? s.ParentStepId == null : s.ParentStepId == parentStepId && s.Branch == branch)
            .OrderBy(s => s.Position)
            .ToListAsync();

        if (siblings.Count == 0)
        {
            return (AutomationLogStatus.Success, null);
        }

        // A branch's own failure doesn't halt THIS scope's remaining siblings (see the Condition
        // case below), but it shouldn't be invisible on the log's summary status either — track
        // the worst branch outcome seen and fold it into what this scope reports once it
        // otherwise finishes cleanly.
        AutomationLogStatus? branchDegradation = null;
        string? branchErrorMessage = null;

        foreach (var step in siblings)
        {
            if (step.StepType == AutomationStepType.Stop)
            {
                await AppendLogResultAsync(logId, new AutomationStepResult(step.Id, "Stop", "success", "stopped"));
                return (AutomationLogStatus.Success, null);
            }

            if (step.StepType == AutomationStepType.Wait)
            {
                var cfg = Deserialize<WaitStepConfig>(step.StepConfigJson) ?? new WaitStepConfig();
                var delay = WaitDelay(cfg);
                _backgroundJobClient.Schedule<AutomationEngine>(
                    engine => engine.ResumeWaitAsync(automation.Id, contactId, JsonSerializer.Serialize(context, JsonOptions), parentStepId, branch, step.Position + 1, logId),
                    delay);
                await AppendLogResultAsync(logId, new AutomationStepResult(step.Id, "Wait", "success", $"waiting {cfg.Amount} {cfg.Unit}"));
                return (AutomationLogStatus.Partial, null);
            }

            if (step.StepType == AutomationStepType.Condition)
            {
                var cfg = Deserialize<ConditionStepConfig>(step.StepConfigJson) ?? new ConditionStepConfig();
                bool taken;
                try
                {
                    taken = await EvaluateConditionAsync(cfg, automation, contactId, context);
                }
                catch (Exception ex)
                {
                    await AppendLogResultAsync(logId, new AutomationStepResult(step.Id, "Condition", "failed", ex.Message));
                    return (AutomationLogStatus.Failed, ex.Message);
                }

                await AppendLogResultAsync(logId, new AutomationStepResult(step.Id, "Condition", "success", $"branch={(taken ? "Yes" : "No")}"));

                // A branch is an independent fork: whether it finishes, fails, or suspends on
                // its own Wait step, that outcome is recorded (AppendLogResultAsync happened
                // inside the recursive call) but never halts THIS scope's remaining siblings —
                // e.g. "if VIP, wait 1h then follow up" alongside "always log to the CRM
                // webhook" shouldn't have the webhook wait on the VIP branch's delay. Its
                // outcome still degrades this scope's own summary status, though — see below.
                var (branchStatus, branchError) = await ExecuteStepsFromAsync(automation, contactId, context, step.Id, taken ? "Yes" : "No", 0, logId);
                if (branchStatus != AutomationLogStatus.Success && (branchDegradation is null || branchStatus == AutomationLogStatus.Failed))
                {
                    branchDegradation = branchStatus;
                    branchErrorMessage = branchError;
                }

                continue;
            }

            try
            {
                var detail = await RunStepAsync(step, automation, contactId, context);
                await AppendLogResultAsync(logId, new AutomationStepResult(step.Id, step.StepType.ToString(), "success", detail));
            }
            catch (Exception ex)
            {
                await AppendLogResultAsync(logId, new AutomationStepResult(step.Id, step.StepType.ToString(), "failed", ex.Message));
                return (AutomationLogStatus.Failed, ex.Message);
            }
        }

        return branchDegradation is null ? (AutomationLogStatus.Success, null) : (branchDegradation.Value, branchErrorMessage);
    }

    private async Task<string> RunStepAsync(AutomationStep step, Automation automation, int? contactId, AutomationContext context)
    {
        // Loaded once per step (not per Interpolate call) so {{contact.firstName}} etc. can
        // personalize a message/template/webhook body without a fresh query per placeholder.
        var interpContact = contactId is not null
            ? await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId)
            : null;

        switch (step.StepType)
        {
            case AutomationStepType.SendMessage:
            {
                var cfg = Deserialize<SendMessageStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing SendMessage config.");
                var phone = await ResolvePhoneAsync(contactId, context);
                var text = Interpolate(cfg.Text, context, interpContact);
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidOperationException("send_message has empty text.");
                }

                var result = await _whatsAppService.SendPlainTextMessageAsync(phone, text);
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorMessage);
                }

                return $"sent ({result.Data})";
            }

            case AutomationStepType.SendTemplate:
            {
                var cfg = Deserialize<SendTemplateStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing SendTemplate config.");
                if (string.IsNullOrWhiteSpace(cfg.TemplateName))
                {
                    throw new InvalidOperationException("send_template needs a template name.");
                }

                // Looked up locally so a media/text header actually gets sent — the step config
                // only ever stores the template's name/params, not its shape, since that shape
                // can change if the template is re-submitted and shouldn't need re-editing every
                // automation that references it.
                var template = await _db.WhatsappTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.TemplateName == cfg.TemplateName);
                var isTextHeader = string.Equals(template?.HeaderFormat, "TEXT", StringComparison.OrdinalIgnoreCase);
                var isMediaHeader = template?.HeaderFormat is "IMAGE" or "VIDEO" or "DOCUMENT";

                var phone = await ResolvePhoneAsync(contactId, context);
                var sendRequest = new TemplateSendRequest
                {
                    TemplateName = cfg.TemplateName,
                    Language = cfg.Language,
                    HeaderFormat = template?.HeaderFormat,
                    HeaderText = isTextHeader && !string.IsNullOrEmpty(cfg.HeaderParam) ? Interpolate(cfg.HeaderParam, context, interpContact) : null,
                    HeaderMediaUrl = isMediaHeader ? template!.HeaderMediaUrl : null,
                    BodyParams = [.. cfg.BodyParams.Select(p => Interpolate(p, context, interpContact))],
                };
                var result = await _whatsAppService.SendTemplateMessageAsync(phone, sendRequest);
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorMessage);
                }

                // Previously this step sent the template but never recorded it anywhere — it
                // didn't show up in the Chat inbox like every other outbound path does, and
                // (more importantly for reporting) there was no row to ever attribute to this
                // template at all. Skipped only if the template got deleted locally between when
                // the automation was configured and when it actually ran — the send itself still
                // succeeded, there's just nothing local to attach the record to.
                if (template is not null)
                {
                    var chat = await ResolveOrCreateChatAsync(phone, contactId);
                    var rendered = TemplateMessageRenderer.ForChatMessage(template, sendRequest);
                    await LogAndBroadcastOutboundMessageAsync(chat, new ChatMessage
                    {
                        ChatId = chat.Id,
                        SenderId = chat.WaNoId ?? "automation",
                        Message = rendered.DisplayText,
                        MessageType = rendered.MediaMessageType ?? "text",
                        Url = rendered.MediaUrl,
                        WhatsappMessageId = result.Data,
                        Status = MessageDeliveryStatus.Sent,
                        SentAt = DateTime.UtcNow,
                        TemplateName = cfg.TemplateName,
                        TimeSent = DateTime.UtcNow,
                        IsRead = true,
                    });
                }

                return $"template sent ({result.Data})";
            }

            case AutomationStepType.SendButtons:
            {
                var cfg = Deserialize<SendButtonsStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing SendButtons config.");
                var phone = await ResolvePhoneAsync(contactId, context);
                var buttons = cfg.Buttons.Select(b => new InteractiveButton(b.Id, b.Title)).ToList();
                var result = await _whatsAppService.SendInteractiveButtonsMessageAsync(phone, Interpolate(cfg.BodyText, context, interpContact), buttons);
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorMessage);
                }

                return $"buttons sent ({result.Data})";
            }

            case AutomationStepType.UpdateContactField:
            {
                var cfg = Deserialize<UpdateContactFieldStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing UpdateContactField config.");
                if (contactId is null)
                {
                    throw new InvalidOperationException("update_contact_field needs a contact.");
                }

                var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId);
                if (contact is null)
                {
                    throw new InvalidOperationException("Contact not found.");
                }

                var value = Interpolate(cfg.Value, context, contact);
                var applied = ApplyContactField(contact, cfg.Field, value);
                if (!applied)
                {
                    return $"field {cfg.Field} not writable from automations";
                }

                contact.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return $"{cfg.Field} updated";
            }

            case AutomationStepType.SendWebhook:
            {
                var cfg = Deserialize<SendWebhookStepConfig>(step.StepConfigJson) ?? throw new InvalidOperationException("Missing SendWebhook config.");
                if (string.IsNullOrWhiteSpace(cfg.Url))
                {
                    throw new InvalidOperationException("send_webhook needs a url.");
                }

                if (!await WebhookUrlGuard.IsDeliverableAsync(cfg.Url))
                {
                    throw new InvalidOperationException("send_webhook: destination not allowed.");
                }

                var body = string.IsNullOrEmpty(cfg.BodyTemplate)
                    ? JsonSerializer.Serialize(context, JsonOptions)
                    : Interpolate(cfg.BodyTemplate, context, interpContact);

                using var request = new HttpRequestMessage(HttpMethod.Post, cfg.Url)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                };
                if (cfg.Headers is not null)
                {
                    foreach (var (key, value) in cfg.Headers)
                    {
                        request.Headers.TryAddWithoutValidation(key, value);
                    }
                }

                var client = _httpClientFactory.CreateClient("AutomationWebhook");
                client.Timeout = TimeSpan.FromSeconds(10);
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

    private static bool ApplyContactField(Models.Entities.Contact contact, string field, string value)
    {
        switch (field)
        {
            case "FirstName": contact.FirstName = value; return true;
            case "LastName": contact.LastName = value; return true;
            case "Company": contact.Company = value; return true;
            case "Email": contact.Email = value; return true;
            case "Description": contact.Description = value; return true;
            case "City": contact.City = value; return true;
            case "State": contact.State = value; return true;
            default: return false;
        }
    }

    private async Task<bool> EvaluateConditionAsync(ConditionStepConfig cfg, Automation automation, int? contactId, AutomationContext context)
    {
        switch (cfg.Subject)
        {
            case "MessageContent":
                return (context.MessageText ?? string.Empty).Contains(cfg.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            case "ContactField":
            {
                if (contactId is null || string.IsNullOrEmpty(cfg.Operand))
                {
                    return false;
                }

                var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId);
                if (contact is null)
                {
                    return false;
                }

                var actual = cfg.Operand switch
                {
                    "FirstName" => contact.FirstName,
                    "LastName" => contact.LastName,
                    "Company" => contact.Company,
                    "Email" => contact.Email,
                    "City" => contact.City,
                    "State" => contact.State,
                    "Type" => contact.Type.ToString(),
                    _ => null,
                };
                return string.Equals(actual ?? string.Empty, cfg.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            case "TimeOfDay":
            {
                var parts = (cfg.Operand ?? string.Empty).Split('-');
                if (parts.Length != 2 || !TryParseHm(parts[0], out var from) || !TryParseHm(parts[1], out var to))
                {
                    return false;
                }

                var now = DateTime.Now;
                var minutes = now.Hour * 60 + now.Minute;
                return from <= to ? minutes >= from && minutes < to : minutes >= from || minutes < to;
            }

            default:
                return false;
        }
    }

    private static bool TryParseHm(string s, out int minutes)
    {
        minutes = 0;
        var parts = s.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m))
        {
            return false;
        }

        minutes = h * 60 + m;
        return true;
    }

    private static bool TriggerMatches(Automation automation, AutomationContext context)
    {
        switch (automation.TriggerType)
        {
            case AutomationTriggerType.KeywordMatch:
            {
                var cfg = Deserialize<KeywordMatchTriggerConfig>(automation.TriggerConfigJson);
                if (cfg is null || cfg.Keywords.Count == 0)
                {
                    return false;
                }

                var text = context.MessageText ?? string.Empty;
                if (text.Length == 0)
                {
                    return false;
                }

                return cfg.MatchType switch
                {
                    "exact" => cfg.Keywords.Any(k => string.Equals(text, k, cfg.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)),
                    "word" => cfg.Keywords.Any(k => MatchesWholeWord(text, k, cfg.CaseSensitive)),
                    _ => cfg.Keywords.Any(k => text.Contains(k, cfg.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)),
                };
            }

            case AutomationTriggerType.InteractiveReply:
            {
                var cfg = Deserialize<InteractiveReplyTriggerConfig>(automation.TriggerConfigJson);
                if (cfg is null || cfg.ReplyIds.Count == 0 || string.IsNullOrEmpty(context.InteractiveReplyId))
                {
                    return false;
                }

                return cfg.ReplyIds.Contains(context.InteractiveReplyId);
            }

            case AutomationTriggerType.FacebookLeadReceived:
            {
                var cfg = Deserialize<FacebookLeadFormTriggerConfig>(automation.TriggerConfigJson);
                // No FormId configured means "any form" — preserves the original unscoped
                // behavior for automations saved before per-form scoping existed.
                return string.IsNullOrEmpty(cfg?.FormId) || string.Equals(cfg.FormId, context.LeadFormId, StringComparison.Ordinal);
            }

            default:
                return true;
        }
    }

    /// <summary>Whole-word match so a one-letter keyword under "contains" doesn't fire on every message containing that letter.</summary>
    private static bool MatchesWholeWord(string text, string keyword, bool caseSensitive)
    {
        if (string.IsNullOrEmpty(keyword))
        {
            return false;
        }

        var escaped = Regex.Escape(keyword);
        var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        var pattern = $@"(?<!\w){escaped}(?!\w)";
        return Regex.IsMatch(text, pattern, options);
    }

    private async Task<string> ResolvePhoneAsync(int? contactId, AutomationContext context)
    {
        if (contactId is not null)
        {
            var phone = await _db.Contacts.AsNoTracking().Where(c => c.Id == contactId).Select(c => c.Phone).FirstOrDefaultAsync();
            if (!string.IsNullOrEmpty(phone))
            {
                return phone;
            }
        }

        if (context.ChatId is not null)
        {
            var receiverId = await _db.Chats.AsNoTracking().Where(c => c.Id == context.ChatId).Select(c => c.ReceiverId).FirstOrDefaultAsync();
            if (!string.IsNullOrEmpty(receiverId))
            {
                return receiverId;
            }
        }

        throw new InvalidOperationException("Cannot resolve a phone number to send to — no contact or chat in context.");
    }

    private async Task<Models.Entities.Chat> ResolveOrCreateChatAsync(string phone, int? contactId)
    {
        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.ReceiverId == phone);
        if (chat is not null)
        {
            return chat;
        }

        var contactName = contactId is not null
            ? await _db.Contacts.Where(c => c.Id == contactId).Select(c => c.FirstName + " " + c.LastName).FirstOrDefaultAsync()
            : null;

        chat = new Models.Entities.Chat
        {
            WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
            ReceiverId = phone,
            Name = string.IsNullOrWhiteSpace(contactName) ? phone : contactName,
        };
        _db.Chats.Add(chat);
        await _db.SaveChangesAsync();
        return chat;
    }

    private async Task LogAndBroadcastOutboundMessageAsync(Models.Entities.Chat chat, ChatMessage message)
    {
        _db.ChatMessages.Add(message);
        chat.LastMessage = message.Message;
        chat.LastMessageTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dto = new ChatMessageDto(message.Id, message.SenderId, message.Message, message.MessageType, message.TimeSent, true, message.Status.ToString(), message.Url);
        await _hubContext.Clients.All.SendAsync("ReceiveMessage", chat.Id, dto);
    }

    private static string Interpolate(string text, AutomationContext context, Models.Entities.Contact? contact = null) =>
        Regex.Replace(text, @"\{\{\s*([\w.]+)\s*\}\}", match =>
        {
            var key = match.Groups[1].Value;
            var parts = key.Split('.', 2);
            if (parts.Length == 2 && parts[0] == "message" && parts[1] == "text")
            {
                return context.MessageText ?? string.Empty;
            }

            if (parts.Length == 2 && parts[0] == "vars" && context.Vars.TryGetValue(parts[1], out var v))
            {
                return v;
            }

            if (parts.Length == 2 && parts[0] == "contact")
            {
                return parts[1] switch
                {
                    "firstName" => contact?.FirstName ?? string.Empty,
                    "lastName" => contact?.LastName ?? string.Empty,
                    "fullName" => contact is null ? string.Empty : $"{contact.FirstName} {contact.LastName}".Trim(),
                    "phone" => contact?.Phone ?? string.Empty,
                    "email" => contact?.Email ?? string.Empty,
                    _ => string.Empty,
                };
            }

            return string.Empty;
        });

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

    private async Task AppendLogResultAsync(int? logId, AutomationStepResult result)
    {
        if (logId is null)
        {
            return;
        }

        var log = await _db.AutomationLogs.FirstOrDefaultAsync(l => l.Id == logId);
        if (log is null)
        {
            return;
        }

        var existing = Deserialize<List<AutomationStepResult>>(log.StepsExecutedJson) ?? [];
        existing.Add(result);
        log.StepsExecutedJson = JsonSerializer.Serialize(existing, JsonOptions);
        await _db.SaveChangesAsync();
    }

    private async Task FinalizeLogAsync(int logId, AutomationLogStatus status, string? errorMessage)
    {
        var log = await _db.AutomationLogs.FirstOrDefaultAsync(l => l.Id == logId);
        if (log is null)
        {
            return;
        }

        log.Status = status;
        if (errorMessage is not null)
        {
            log.ErrorMessage = errorMessage;
        }

        await _db.SaveChangesAsync();
    }
}
