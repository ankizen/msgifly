namespace Msgifly.Web.Authorization;

/// <summary>
/// The full permission list, carried over from the original app's PermissionSeeder (master doc §8.3) —
/// effectively the module inventory. Referenced by [Authorize(Policy = Permissions.X)] and by DbSeeder.
///
/// Deliberately excludes api_key.* — API Keys is gated to the is_admin superuser flag only (see
/// ApiKeysController, policy "MasterAdminOnly"), not the normal role/user permission grant this
/// list feeds into. A granted API key can now drive MCP tools with real send/create/automation
/// power, so it isn't delegable through Roles like everything else here.
/// </summary>
public static class Permissions
{
    public static readonly string[] All =
    [
        "source.view", "source.create", "source.edit", "source.delete",
        "ai_prompt.view", "ai_prompt.create", "ai_prompt.edit", "ai_prompt.delete",
        "canned_reply.view", "canned_reply.create", "canned_reply.edit", "canned_reply.delete",
        "connect_account.view", "connect_account.connect", "connect_account.disconnect",
        "template.view", "template.load_template",
        "flow.view", "flow.create", "flow.edit", "flow.delete",
        "billing.view",
        "group.view", "group.create", "group.edit", "group.delete",
        "campaigns.view", "campaigns.create", "campaigns.edit", "campaigns.delete", "campaigns.show_campaign",
        "chat.view", "chat.read_only",
        "activity_log.view", "activity_log.delete",
        "msgifly_settings.view", "msgifly_settings.edit",
        "bulk_campaigns.send",
        "role.view", "role.create", "role.edit", "role.delete",
        "status.view", "status.create", "status.edit", "status.delete",
        "contact.view", "contact.create", "contact.edit", "contact.delete", "contact.bulk_import",
        "system_settings.view", "system_settings.edit",
        "user.view", "user.create", "user.edit", "user.delete",
        "email_template.view", "email_template.edit",
        "automation.view", "automation.create", "automation.edit", "automation.delete",
        "workspace.view", "workspace.create", "workspace.edit",
        // No email_subscriber.* — Contact IS the email subscriber (Leads & CRM's contact.*
        // permissions already gate list/tag/email-status management on the Contact form).
        "email_list.view", "email_list.create", "email_list.edit", "email_list.delete",
        "email_tag.view", "email_tag.create", "email_tag.edit", "email_tag.delete",
        "email_campaign.view", "email_campaign.create", "email_campaign.edit", "email_campaign.delete", "email_campaign.send",
        "email_smtp.view", "email_smtp.create", "email_smtp.edit", "email_smtp.delete",
        "email_automation.view", "email_automation.create", "email_automation.edit", "email_automation.delete",
        "email_sequence.view", "email_sequence.create", "email_sequence.edit", "email_sequence.delete",
    ];
}
