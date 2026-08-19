using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.ApiKeys;
using Msgifly.Web.Services.Email;
using Msgifly.Web.Services.EmailAutomations;
using Msgifly.Web.Services.EmailSequences;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Services.Mcp;

/// <summary>Email Marketing MCP surface. Contact IS the email subscriber (no separate table) — every
/// tool here that targets "a subscriber" takes the same contactId the WhatsApp tools use; find one
/// by email with find_email_subscriber. Modeled on AutomationMcpTools' shape, independent stack.</summary>
[McpServerToolType]
public class EmailMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);

    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly EmailAudienceResolver _audienceResolver;
    private readonly EmailSequenceService _sequenceService;

    public EmailMcpTools(
        ApplicationDbContext db,
        ICurrentWorkspaceAccessor workspaceAccessor,
        IHttpContextAccessor httpContextAccessor,
        EmailAudienceResolver audienceResolver,
        EmailSequenceService sequenceService)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
        _httpContextAccessor = httpContextAccessor;
        _audienceResolver = audienceResolver;
        _sequenceService = sequenceService;
    }

    [McpServerTool(Name = "find_email_subscriber")]
    [Description("Looks up a contact by email address and returns its contactId plus email opt-in status, list, and tag membership. Use this to get the contactId every other email tool here needs.")]
    public async Task<object> FindEmailSubscriberAsync(
        [Description("Exact email address to look up")] string email)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailRead);

        var trimmed = email.Trim();
        var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Email == trimmed);
        if (contact is null)
        {
            return new { found = false };
        }

        var listNames = await _db.EmailSubscriberLists.AsNoTracking()
            .Where(l => l.SubscriberId == contact.Id).Select(l => l.List.Name).ToListAsync();
        var tagNames = await _db.EmailSubscriberTags.AsNoTracking()
            .Where(t => t.SubscriberId == contact.Id).Select(t => t.Tag.Name).ToListAsync();

        return new
        {
            found = true,
            contactId = contact.Id,
            name = (contact.FirstName + " " + contact.LastName).Trim(),
            email = contact.Email,
            emailStatus = contact.EmailStatus.ToString(),
            lists = listNames,
            tags = tagNames,
        };
    }

    [McpServerTool(Name = "set_email_subscriber_status")]
    [Description("Sets a contact's email opt-in status. Only Subscribed and Transactional contacts are included in bulk campaign sends. Unrelated to the contact's CRM pipeline status.")]
    public async Task<object> SetEmailSubscriberStatusAsync(
        [Description("Contact id, from find_email_subscriber")] int contactId,
        [Description("One of: Subscribed, Pending, Unsubscribed, Bounced, Complained, Transactional")] string status)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailWrite);

        if (!Enum.TryParse<EmailSubscriberStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            throw new McpException($"Unknown status '{status}'. Use one of: Subscribed, Pending, Unsubscribed, Bounced, Complained, Transactional.");
        }

        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId);
        if (contact is null)
        {
            throw new McpException($"No contact with id {contactId} in this workspace.");
        }

        contact.EmailStatus = parsedStatus;
        contact.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new { success = true, contactId, emailStatus = contact.EmailStatus.ToString() };
    }

    [McpServerTool(Name = "list_email_lists")]
    [Description("Lists this workspace's email lists with their member counts.")]
    public async Task<object> ListEmailListsAsync()
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailRead);

        var lists = await _db.EmailLists.AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l => new { id = l.Id, name = l.Name, description = l.Description, memberCount = l.Members.Count })
            .ToListAsync();

        return new { lists };
    }

    [McpServerTool(Name = "list_email_tags")]
    [Description("Lists this workspace's email tags with their member counts.")]
    public async Task<object> ListEmailTagsAsync()
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailRead);

        var tags = await _db.EmailTags.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new { id = t.Id, name = t.Name, memberCount = t.Members.Count })
            .ToListAsync();

        return new { tags };
    }

    [McpServerTool(Name = "add_contact_to_email_list")]
    [Description("Adds a contact to an email list — e.g. after a Facebook Lead comes in with an email, add them to a list to bring them into that list's auto-enrolling sequence or SubscriberAdded automation. No-ops if already a member. Fails if the contact has no email address.")]
    public async Task<object> AddContactToEmailListAsync(
        [Description("Contact id, from find_email_subscriber")] int contactId,
        [Description("List id, from list_email_lists")] int listId)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailWrite);

        var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId);
        if (contact is null)
        {
            throw new McpException($"No contact with id {contactId} in this workspace.");
        }

        if (string.IsNullOrWhiteSpace(contact.Email))
        {
            throw new McpException($"Contact {contactId} has no email address — add one first.");
        }

        var listExists = await _db.EmailLists.AnyAsync(l => l.Id == listId);
        if (!listExists)
        {
            throw new McpException($"No email list with id {listId} in this workspace.");
        }

        var alreadyMember = await _db.EmailSubscriberLists.AnyAsync(l => l.SubscriberId == contactId && l.ListId == listId);
        if (!alreadyMember)
        {
            _db.EmailSubscriberLists.Add(new EmailSubscriberList { SubscriberId = contactId, ListId = listId });
            await _db.SaveChangesAsync();
        }

        return new { success = true, contactId, listId };
    }

    [McpServerTool(Name = "remove_contact_from_email_list")]
    [Description("Removes a contact from an email list. No-ops if not a member.")]
    public async Task<object> RemoveContactFromEmailListAsync(
        [Description("Contact id")] int contactId,
        [Description("List id")] int listId)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailWrite);

        var membership = await _db.EmailSubscriberLists.FirstOrDefaultAsync(l => l.SubscriberId == contactId && l.ListId == listId);
        if (membership is not null)
        {
            _db.EmailSubscriberLists.Remove(membership);
            await _db.SaveChangesAsync();
        }

        return new { success = true, contactId, listId };
    }

    [McpServerTool(Name = "add_email_tag_to_contact")]
    [Description("Tags a contact for email marketing — e.g. to scope a TagApplied automation. No-ops if already tagged. Fails if the contact has no email address.")]
    public async Task<object> AddEmailTagToContactAsync(
        [Description("Contact id, from find_email_subscriber")] int contactId,
        [Description("Tag id, from list_email_tags")] int tagId)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailWrite);

        var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId);
        if (contact is null)
        {
            throw new McpException($"No contact with id {contactId} in this workspace.");
        }

        if (string.IsNullOrWhiteSpace(contact.Email))
        {
            throw new McpException($"Contact {contactId} has no email address — add one first.");
        }

        var tagExists = await _db.EmailTags.AnyAsync(t => t.Id == tagId);
        if (!tagExists)
        {
            throw new McpException($"No email tag with id {tagId} in this workspace.");
        }

        var alreadyTagged = await _db.EmailSubscriberTags.AnyAsync(t => t.SubscriberId == contactId && t.TagId == tagId);
        if (!alreadyTagged)
        {
            _db.EmailSubscriberTags.Add(new EmailSubscriberTag { SubscriberId = contactId, TagId = tagId });
            await _db.SaveChangesAsync();
        }

        return new { success = true, contactId, tagId };
    }

    [McpServerTool(Name = "remove_email_tag_from_contact")]
    [Description("Removes an email tag from a contact. No-ops if not tagged.")]
    public async Task<object> RemoveEmailTagFromContactAsync(
        [Description("Contact id")] int contactId,
        [Description("Tag id")] int tagId)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailWrite);

        var membership = await _db.EmailSubscriberTags.FirstOrDefaultAsync(t => t.SubscriberId == contactId && t.TagId == tagId);
        if (membership is not null)
        {
            _db.EmailSubscriberTags.Remove(membership);
            await _db.SaveChangesAsync();
        }

        return new { success = true, contactId, tagId };
    }

    [McpServerTool(Name = "list_email_campaigns")]
    [Description("Lists this workspace's broadcast email campaigns with their status and recipient count.")]
    public async Task<object> ListEmailCampaignsAsync()
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailRead);

        var campaigns = await _db.EmailCampaigns.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                id = c.Id,
                name = c.Name,
                status = c.Status.ToString(),
                subject = c.Subject,
                sendNow = c.SendNow,
                scheduledAt = c.ScheduledAt,
                recipientCount = c.Recipients.Count,
                sentCount = c.Recipients.Count(r => r.Status == EmailCampaignRecipientStatus.Sent),
                failedCount = c.Recipients.Count(r => r.Status == EmailCampaignRecipientStatus.Failed),
            })
            .ToListAsync();

        return new { campaigns };
    }

    [McpServerTool(Name = "create_email_campaign")]
    [Description("""
        Creates and schedules a broadcast email campaign. Recipients are resolved and materialized
        immediately from the include/exclude list and tag ids (only contacts with EmailStatus
        Subscribed or Transactional are ever included) — the campaign then sends on the next
        dispatch sweep (within about a minute) if sendNow is true, or at scheduledAt otherwise.
        Subject/bodyHtml may use {{subscriber.firstName}}, {{subscriber.lastName}}, {{subscriber.email}}
        for personalization, and bodyHtml should include {{unsubscribe_link}} somewhere.
        """)]
    public async Task<object> CreateEmailCampaignAsync(
        [Description("Campaign name, shown in the dashboard")] string name,
        [Description("From name shown to recipients")] string fromName,
        [Description("From email address")] string fromEmail,
        [Description("Email subject line")] string subject,
        [Description("Email body as HTML")] string bodyHtml,
        [Description("Send on the next dispatch sweep (within ~1 minute) instead of at a scheduled time")] bool sendNow = true,
        [Description("ISO 8601 datetime to send at, when sendNow is false")] DateTime? scheduledAt = null,
        [Description("Target every bulk-sendable subscriber in the workspace, ignoring listIds/tagIds")] bool selectAll = false,
        [Description("Only include subscribers on any of these list ids")] List<int>? listIds = null,
        [Description("Only include subscribers tagged with any of these tag ids")] List<int>? tagIds = null,
        [Description("Exclude subscribers on any of these list ids, even if matched above")] List<int>? excludeListIds = null,
        [Description("Exclude subscribers tagged with any of these tag ids, even if matched above")] List<int>? excludeTagIds = null)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailWrite);

        listIds ??= [];
        tagIds ??= [];
        if (!selectAll && listIds.Count == 0 && tagIds.Count == 0)
        {
            throw new McpException("Set selectAll true, or pass at least one listId/tagId to target.");
        }

        if (!sendNow && scheduledAt is null)
        {
            throw new McpException("Pass scheduledAt, or set sendNow true.");
        }

        var campaign = new EmailCampaign
        {
            WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
            Name = name.Trim(),
            FromName = fromName,
            FromEmail = fromEmail,
            Subject = subject,
            BodyHtml = bodyHtml,
            SendNow = sendNow,
            ScheduledAt = sendNow ? null : scheduledAt,
            SelectAll = selectAll,
            IncludeListIdsJson = listIds.Count > 0 ? JsonSerializer.Serialize(listIds) : null,
            IncludeTagIdsJson = tagIds.Count > 0 ? JsonSerializer.Serialize(tagIds) : null,
            ExcludeListIdsJson = excludeListIds is { Count: > 0 } ? JsonSerializer.Serialize(excludeListIds) : null,
            ExcludeTagIdsJson = excludeTagIds is { Count: > 0 } ? JsonSerializer.Serialize(excludeTagIds) : null,
            Status = EmailCampaignStatus.Scheduled,
        };
        _db.EmailCampaigns.Add(campaign);
        await _db.SaveChangesAsync(); // need campaign.Id for recipient FKs

        var subscriberIds = await _audienceResolver.ResolveSubscriberIdsAsync(campaign);
        var recipients = subscriberIds.Select(subscriberId => new EmailCampaignRecipient
        {
            CampaignId = campaign.Id,
            SubscriberId = subscriberId,
            Status = EmailCampaignRecipientStatus.Pending,
            TrackingToken = Guid.NewGuid().ToString("N"),
        }).ToList();
        _db.EmailCampaignRecipients.AddRange(recipients);
        await _db.SaveChangesAsync();

        return new { success = true, campaignId = campaign.Id, name = campaign.Name, recipientCount = recipients.Count };
    }

    [McpServerTool(Name = "list_email_automations")]
    [Description("Lists this workspace's email automations with their trigger and active state.")]
    public async Task<object> ListEmailAutomationsAsync()
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailRead);

        var automations = await _db.EmailAutomations.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                id = a.Id,
                name = a.Name,
                description = a.Description,
                triggerType = a.TriggerType.ToString(),
                isActive = a.IsActive,
                stepCount = a.Steps.Count,
            })
            .ToListAsync();

        return new { automations };
    }

    [McpServerTool(Name = "list_email_automation_logs")]
    [Description("Lists an email automation's run history, most recent first, including which contact each run was for.")]
    public async Task<object> ListEmailAutomationLogsAsync(
        [Description("Automation id, from list_email_automations")] int automationId,
        [Description("Filter to only this status: Success | Failed | Partial. Omit for all.")] string? status = null,
        [Description("Max rows to return, most recent first")] int limit = 50)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailRead);

        var query = _db.EmailAutomationLogs.AsNoTracking().Where(l => l.AutomationId == automationId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<EmailAutomationLogStatus>(status, ignoreCase: true, out var parsedStatus))
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
                contactId = l.SubscriberId,
                contactEmail = l.Subscriber.Email,
                // Array of {stepId, stepType, status, detail} for each step that ran — the failed
                // entry's "detail" is where the actual error message lives, there's no separate column.
                stepResultsJson = l.ResultJson,
            })
            .ToListAsync();

        return new { logs };
    }

    [McpServerTool(Name = "create_email_automation")]
    [Description("""
        Creates an email automation: a trigger plus an ordered tree of steps, independent of the
        WhatsApp automation engine. Runs against contacts (Contact IS the email subscriber) only —
        a contact with no email is skipped when the trigger fires.

        stepsJsonTree is a JSON array of step nodes, each shaped:
          { "type": "<StepType>", "config": { ...per-type shape below... }, "yes": [...], "no": [...] }
        "yes"/"no" are only used on Condition nodes; omit them on every other step type. Steps run in
        array order; a Condition node ends its parent's linear chain and branches instead.

        Per-StepType config shape:
          SendEmail:              { "subject": "...", "bodyHtml": "..." }
          Wait:                   { "amount": 1, "unit": "minutes" }   (unit: minutes | hours | days)
          Condition:               { "subject": "SubscriberField", "operand": "EmailStatus", "value": "Subscribed" }
                                     subject is one of: SubscriberField (operand = field name, value =
                                     expected value), HasTag (operand = tag id as a string), HasList
                                     (operand = list id as a string)
          AddTag / RemoveTag:      { "tagId": 1 }
          AddToList / RemoveFromList: { "listId": 1 }
          UpdateSubscriberField:   { "field": "FirstName", "value": "..." }
          Webhook:                 { "url": "https://...", "bodyTemplate": "optional JSON template" }
          Stop:                    {}

        Text fields in SendEmail may use {{subscriber.firstName}}, {{subscriber.lastName}},
        {{subscriber.email}} for personalization.

        Example: send an email, wait a day, then send a follow-up only if still Subscribed:
          [
            { "type": "SendEmail", "config": { "subject": "Welcome {{subscriber.firstName}}", "bodyHtml": "<p>Hi!</p>" } },
            { "type": "Wait", "config": { "amount": 1, "unit": "days" } },
            { "type": "Condition", "config": { "subject": "SubscriberField", "operand": "EmailStatus", "value": "Subscribed" },
              "yes": [ { "type": "SendEmail", "config": { "subject": "Still there?", "bodyHtml": "<p>Following up.</p>" } } ],
              "no": [] }
          ]
        """)]
    public async Task<object> CreateEmailAutomationAsync(
        [Description("Automation name, shown in the dashboard")] string name,
        [Description("One of: SubscriberAdded, TagApplied, ListApplied")] string triggerType,
        [Description("The step tree — see the tool description above for the exact JSON shape")] string stepsJsonTree,
        [Description("Optional note shown only in the dashboard")] string? description = null,
        [Description("For SubscriberAdded/ListApplied: scope to this list id. Omit for SubscriberAdded to match any list.")] int? listId = null,
        [Description("For TagApplied: which tag id triggers this automation")] int? tagId = null,
        [Description("Whether the automation should run immediately. Defaults to false so you can review it in the dashboard first.")] bool isActive = false)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailWrite);

        if (!Enum.TryParse<EmailAutomationTriggerType>(triggerType, ignoreCase: true, out var parsedTriggerType))
        {
            throw new McpException($"Unknown triggerType '{triggerType}'. Use one of: SubscriberAdded, TagApplied, ListApplied.");
        }

        if (parsedTriggerType == EmailAutomationTriggerType.TagApplied && tagId is null)
        {
            throw new McpException("tagId is required when triggerType is TagApplied.");
        }

        if (parsedTriggerType == EmailAutomationTriggerType.ListApplied && listId is null)
        {
            throw new McpException("listId is required when triggerType is ListApplied.");
        }

        List<EmailAutomationStepNode>? tree;
        try
        {
            tree = JsonSerializer.Deserialize<List<EmailAutomationStepNode>>(stepsJsonTree, JsonOptions);
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
            EmailAutomationTreeBuilder.ValidateTree(tree);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }

        var triggerConfigJson = parsedTriggerType == EmailAutomationTriggerType.TagApplied
            ? JsonSerializer.Serialize(new TagAppliedTriggerConfig { TagId = tagId }, JsonOptions)
            : JsonSerializer.Serialize(new ListScopedTriggerConfig { ListId = listId }, JsonOptions);

        var automation = new EmailAutomation
        {
            WorkspaceId = _workspaceAccessor.WorkspaceId!.Value,
            Name = name.Trim(),
            Description = description,
            TriggerType = parsedTriggerType,
            TriggerConfigJson = triggerConfigJson,
            IsActive = isActive,
        };
        _db.EmailAutomations.Add(automation);
        await _db.SaveChangesAsync(); // need automation.Id for step FKs

        var steps = new List<EmailAutomationStep>();
        EmailAutomationTreeBuilder.FlattenTree(tree, automation.Id, null, null, steps);
        _db.EmailAutomationSteps.AddRange(steps);
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

    [McpServerTool(Name = "set_email_automation_active")]
    [Description("Activates or pauses an email automation. A paused automation never runs, even if its trigger fires.")]
    public async Task<object> SetEmailAutomationActiveAsync(
        [Description("Automation id, from list_email_automations")] int automationId,
        [Description("true to activate, false to pause")] bool isActive)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailWrite);

        var automation = await _db.EmailAutomations.FirstOrDefaultAsync(a => a.Id == automationId);
        if (automation is null)
        {
            throw new McpException($"No email automation with id {automationId} in this workspace.");
        }

        automation.IsActive = isActive;
        automation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new { success = true, automationId = automation.Id, isActive = automation.IsActive };
    }

    [McpServerTool(Name = "delete_email_automation")]
    [Description("Permanently deletes an email automation and its run history. This can't be undone.")]
    public async Task<object> DeleteEmailAutomationAsync(
        [Description("Automation id, from list_email_automations")] int automationId)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailWrite);

        var automation = await _db.EmailAutomations.FirstOrDefaultAsync(a => a.Id == automationId);
        if (automation is null)
        {
            throw new McpException($"No email automation with id {automationId} in this workspace.");
        }

        _db.EmailAutomations.Remove(automation);
        await _db.SaveChangesAsync();

        return new { success = true };
    }

    [McpServerTool(Name = "list_email_sequences")]
    [Description("Lists this workspace's email drip sequences with their mail count and active enrollee count.")]
    public async Task<object> ListEmailSequencesAsync()
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailRead);

        var sequences = await _db.EmailSequences.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Name, Status = s.Status.ToString(), s.AutoEnrollListId, MailCount = s.Mails.Count })
            .ToListAsync();

        var sequenceIds = sequences.Select(s => s.Id).ToList();
        var activeCounts = await _db.EmailSequenceSubscribers.AsNoTracking()
            .Where(sub => sequenceIds.Contains(sub.SequenceId) && sub.Status == EmailSequenceSubscriberStatus.Active)
            .GroupBy(sub => sub.SequenceId)
            .Select(g => new { SequenceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SequenceId, x => x.Count);

        return new
        {
            sequences = sequences.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                status = s.Status,
                autoEnrollListId = s.AutoEnrollListId,
                mailCount = s.MailCount,
                activeEnrollees = activeCounts.GetValueOrDefault(s.Id),
            }),
        };
    }

    [McpServerTool(Name = "add_contact_to_email_sequence")]
    [Description("Enrolls a contact into an email drip sequence, starting from its first mail. No-ops if already enrolled, if the sequence has no mails yet, or if the contact has no email address.")]
    public async Task<object> AddContactToEmailSequenceAsync(
        [Description("Contact id, from find_email_subscriber")] int contactId,
        [Description("Sequence id, from list_email_sequences")] int sequenceId)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailWrite);

        var sequenceExists = await _db.EmailSequences.AnyAsync(s => s.Id == sequenceId);
        if (!sequenceExists)
        {
            throw new McpException($"No email sequence with id {sequenceId} in this workspace.");
        }

        await _sequenceService.SubscribeAsync(sequenceId, contactId);
        return new { success = true, contactId, sequenceId };
    }

    [McpServerTool(Name = "remove_contact_from_email_sequence")]
    [Description("Cancels a contact's active enrollment in an email drip sequence. No-ops if not actively enrolled.")]
    public async Task<object> RemoveContactFromEmailSequenceAsync(
        [Description("Contact id")] int contactId,
        [Description("Sequence id")] int sequenceId)
    {
        _httpContextAccessor.RequireScope(ApiScopes.EmailWrite);

        await _sequenceService.UnsubscribeAsync(sequenceId, contactId);
        return new { success = true, contactId, sequenceId };
    }
}
