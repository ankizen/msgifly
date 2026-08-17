using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.ApiKeys;
using Msgifly.Web.Services.Automations;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Services.Mcp;

[McpServerToolType]
public class AutomationMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);

    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AutomationEngine _automationEngine;

    public AutomationMcpTools(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor, IHttpContextAccessor httpContextAccessor, AutomationEngine automationEngine)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
        _httpContextAccessor = httpContextAccessor;
        _automationEngine = automationEngine;
    }

    [McpServerTool(Name = "list_automations")]
    [Description("Lists this workspace's automations with their trigger, active state, and step/run counts.")]
    public async Task<object> ListAutomationsAsync()
    {
        _httpContextAccessor.RequireScope(ApiScopes.AutomationsRead);

        var automations = await _db.Automations.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                id = a.Id,
                name = a.Name,
                description = a.Description,
                triggerType = a.TriggerType.ToString(),
                isActive = a.IsActive,
                executionCount = a.ExecutionCount,
                lastExecutedAt = a.LastExecutedAt,
                stepCount = a.Steps.Count,
            })
            .ToListAsync();

        return new { automations };
    }

    [McpServerTool(Name = "list_automation_logs")]
    [Description("Lists an automation's run history, most recent first, including which contact each run was for — use this to find which specific leads a failed run affected (e.g. after a bug fix) before retrying them.")]
    public async Task<object> ListAutomationLogsAsync(
        [Description("Automation id, from list_automations")] int automationId,
        [Description("Filter to only this status: Success | Failed | Partial. Omit for all.")] string? status = null,
        [Description("Max rows to return, most recent first")] int limit = 50)
    {
        _httpContextAccessor.RequireScope(ApiScopes.AutomationsRead);

        var query = _db.AutomationLogs.AsNoTracking().Where(l => l.AutomationId == automationId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AutomationLogStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                throw new McpException($"Unknown status '{status}'. Use one of: Success, Failed, Partial.");
            }

            query = query.Where(l => l.Status == parsedStatus);
        }

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(l => new
            {
                logId = l.Id,
                createdAt = l.CreatedAt,
                status = l.Status.ToString(),
                triggerEvent = l.TriggerEvent,
                errorMessage = l.ErrorMessage,
                contactId = l.ContactId,
                contactPhone = l.Contact != null ? l.Contact.Phone : null,
                contactName = l.Contact != null ? (l.Contact.FirstName + " " + l.Contact.LastName).Trim() : null,
            })
            .ToListAsync();

        return new { logs };
    }

    [McpServerTool(Name = "retry_automation_for_contact")]
    [Description("""
        Re-runs an automation's full step tree from the start for one specific contact — a real
        run (real sends, real Wait scheduling, real Condition evaluation later), not a simulation.
        Use after fixing a bug that caused real runs to fail (check list_automation_logs first to
        find affected contactIds), so those leads get the complete intended sequence rather than
        just a one-off resend of the first message. Doesn't require the automation to be Active,
        and doesn't inflate its ExecutionCount/LastExecutedAt stats (those reflect real trigger
        fires only) — the run is logged with triggerEvent "Retry" so it's visually distinguishable
        from a normal fire on the automation's Logs page.
        """)]
    public async Task<object> RetryAutomationForContactAsync(
        [Description("Automation id, from list_automations")] int automationId,
        [Description("Contact id to retry — from list_automation_logs's contactId field")] int contactId)
    {
        _httpContextAccessor.RequireScope(ApiScopes.AutomationsWrite);

        var contactExists = await _db.Contacts.AnyAsync(c => c.Id == contactId);
        if (!contactExists)
        {
            throw new McpException($"No contact with id {contactId} in this workspace.");
        }

        var (status, errorMessage) = await _automationEngine.RunAutomationForTestAsync(automationId, contactId, "Retry");
        return new { success = status == AutomationLogStatus.Success, status = status.ToString(), errorMessage };
    }

    [McpServerTool(Name = "create_automation")]
    [Description("""
        Creates a WhatsApp automation: a trigger plus an ordered tree of steps. A template used in
        a SendTemplate step must already be status 'Approved' (check with list_templates first) —
        Meta will reject a send against anything else.

        IMPORTANT: every SendTemplate step's bodyParams array length must exactly match that
        template's bodyParamsCount (from list_templates) — a {{1}} placeholder in the body needs
        exactly one entry in bodyParams, even if it's just "{{contact.firstName}}". This is
        validated at creation time and will reject the whole call with a clear message if it
        doesn't match, rather than letting a mismatched automation go live and fail silently on
        every real send with Meta's cryptic "(#132000) Number of parameters does not match".
        Likewise, headerParam is required whenever the template's header is TEXT with a
        placeholder (headerParamsCount > 0) — image/video/document headers never need headerParam,
        they're resolved automatically from the template's own stored media.

        IMPORTANT session-window rule: SendMessage and SendButtons only work within 24 hours of the
        customer's last inbound message. For a trigger that starts with no open conversation
        (FacebookLeadReceived, NewContactCreated), the FIRST step must be SendTemplate, never
        SendMessage or SendButtons.

        stepsJsonTree is a JSON array of step nodes, each shaped:
          { "type": "<StepType>", "config": { ...per-type shape below... }, "yes": [...], "no": [...] }
        "yes"/"no" are only used on Condition nodes (each an array of the same node shape, for that
        branch); omit them on every other step type. Steps run in array order; a Condition node ends
        its parent's linear chain and branches instead.

        Per-StepType config shape:
          SendMessage:        { "text": "..." }
          SendTemplate:       { "templateName": "...", "language": "en_US", "headerParam": "...", "bodyParams": ["..."] }
                                 bodyParams must have exactly bodyParamsCount entries — see above.
          SendButtons:        { "bodyText": "...", "buttons": [{ "id": "yes", "title": "Yes" }] }  (max 3 buttons)
          Wait:                { "amount": 1, "unit": "minutes" }   (unit: minutes | hours | days)
          Condition:           { "subject": "MessageContent", "operand": "", "value": "price" }
                                 subject is one of: MessageContent (only needs value), ContactField
                                 (operand = field name: FirstName/LastName/Company/Email/City/State/
                                 Type; value = value to match), TimeOfDay (operand = "HH:mm-HH:mm";
                                 no value), TemplateClicked (needs neither operand nor value — true
                                 if the last template sent to this contact was clicked)
          UpdateContactField:  { "field": "FirstName", "value": "..." }
          SendWebhook:         { "url": "https://...", "bodyTemplate": "optional JSON template" }
          Stop:                {}

        Text fields in SendMessage/SendTemplate/SendButtons may use {{contact.firstName}},
        {{contact.lastName}}, {{contact.fullName}}, {{contact.phone}} for personalization.

        Example stepsJsonTree for "send a template with one {{1}} body variable, then branch on
        whether they clicked it" (both salonsteps_lead_welcome and salonsteps_lead_nudge have
        bodyParamsCount: 1 in this example, hence one entry each in bodyParams):
          [
            { "type": "SendTemplate", "config": { "templateName": "salonsteps_lead_welcome", "language": "en_US", "bodyParams": ["{{contact.firstName}}"] } },
            { "type": "Wait", "config": { "amount": 1, "unit": "hours" } },
            { "type": "Condition", "config": { "subject": "TemplateClicked" },
              "yes": [ { "type": "SendTemplate", "config": { "templateName": "salonsteps_lead_clicked_followup", "language": "en_US", "bodyParams": ["{{contact.firstName}}"] } } ],
              "no":  [ { "type": "SendTemplate", "config": { "templateName": "salonsteps_lead_nudge", "language": "en_US", "bodyParams": ["{{contact.firstName}}"] } } ] }
          ]
        """)]
    public async Task<object> CreateAutomationAsync(
        [Description("Automation name, shown in the dashboard")] string name,
        [Description("One of: InboundMessage, FirstInboundMessage, KeywordMatch, NewContactCreated, InteractiveReply, FacebookLeadReceived")] string triggerType,
        [Description("The step tree — see the tool description above for the exact JSON shape")] string stepsJsonTree,
        [Description("Optional note shown only in the dashboard, not sent to anyone")] string? description = null,
        [Description("Comma-separated keywords, only used when triggerType is KeywordMatch")] string? keywordsCsv = null,
        [Description("contains | exact | word — only used when triggerType is KeywordMatch")] string keywordMatchType = "contains",
        [Description("Only used when triggerType is KeywordMatch")] bool keywordCaseSensitive = false,
        [Description("Comma-separated button/list-row ids, only used when triggerType is InteractiveReply")] string? interactiveReplyIdsCsv = null,
        [Description("Facebook Lead Ads form id to scope this to, only used when triggerType is FacebookLeadReceived. Leave empty to match any form.")] string? leadFormId = null,
        [Description("Whether the automation should run immediately. Defaults to false so you can review it in the dashboard first.")] bool isActive = false)
    {
        _httpContextAccessor.RequireScope(ApiScopes.AutomationsWrite);

        if (!Enum.TryParse<AutomationTriggerType>(triggerType, ignoreCase: true, out var parsedTriggerType))
        {
            throw new McpException($"Unknown triggerType '{triggerType}'. Use one of: InboundMessage, FirstInboundMessage, KeywordMatch, NewContactCreated, InteractiveReply, FacebookLeadReceived.");
        }

        List<AutomationStepNode>? tree;
        try
        {
            tree = JsonSerializer.Deserialize<List<AutomationStepNode>>(stepsJsonTree, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new McpException($"stepsJsonTree is not valid JSON: {ex.Message}");
        }

        if (tree is null || tree.Count == 0)
        {
            throw new McpException("stepsJsonTree must contain at least one step.");
        }

        try
        {
            AutomationTreeBuilder.ValidateTree(tree, depth: 0);
            await AutomationTreeBuilder.ValidateTemplateParamsAsync(tree, _db);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }

        var triggerConfigJson = AutomationTreeBuilder.BuildTriggerConfigJson(
            parsedTriggerType, keywordsCsv, keywordMatchType, keywordCaseSensitive, interactiveReplyIdsCsv, leadFormId);

        var automation = new Automation
        {
            WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
            Name = name.Trim(),
            Description = description,
            TriggerType = parsedTriggerType,
            TriggerConfigJson = triggerConfigJson,
            IsActive = isActive,
        };
        _db.Automations.Add(automation);
        await _db.SaveChangesAsync(); // need automation.Id for step FKs

        var steps = new List<AutomationStep>();
        AutomationTreeBuilder.FlattenTree(tree, automation.Id, null, null, steps);
        _db.AutomationSteps.AddRange(steps);
        await _db.SaveChangesAsync();

        return new
        {
            success = true,
            automationId = automation.Id,
            name = automation.Name,
            isActive = automation.IsActive,
            stepCount = steps.Count,
        };
    }

    [McpServerTool(Name = "set_automation_active")]
    [Description("Activates or pauses an automation. A paused automation never runs, even if its trigger fires.")]
    public async Task<object> SetAutomationActiveAsync(
        [Description("Automation id, from list_automations")] int automationId,
        [Description("true to activate, false to pause")] bool isActive)
    {
        _httpContextAccessor.RequireScope(ApiScopes.AutomationsWrite);

        var automation = await _db.Automations.FirstOrDefaultAsync(a => a.Id == automationId);
        if (automation is null)
        {
            throw new McpException($"No automation with id {automationId} in this workspace.");
        }

        automation.IsActive = isActive;
        automation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new { success = true, automationId = automation.Id, isActive = automation.IsActive };
    }

    [McpServerTool(Name = "delete_automation")]
    [Description("Permanently deletes an automation and its run history. This can't be undone.")]
    public async Task<object> DeleteAutomationAsync(
        [Description("Automation id, from list_automations")] int automationId)
    {
        _httpContextAccessor.RequireScope(ApiScopes.AutomationsWrite);

        var automation = await _db.Automations.FirstOrDefaultAsync(a => a.Id == automationId);
        if (automation is null)
        {
            throw new McpException($"No automation with id {automationId} in this workspace.");
        }

        _db.Automations.Remove(automation);
        await _db.SaveChangesAsync();

        return new { success = true };
    }
}
