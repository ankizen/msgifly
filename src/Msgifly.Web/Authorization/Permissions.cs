namespace Msgifly.Web.Authorization;

/// <summary>
/// The full permission list, carried over from the original app's PermissionSeeder (master doc §8.3) —
/// effectively the module inventory. Referenced by [Authorize(Policy = Permissions.X)] and by DbSeeder.
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
        "api_key.view", "api_key.create", "api_key.delete",
        "workspace.view", "workspace.create", "workspace.edit",
    ];
}
