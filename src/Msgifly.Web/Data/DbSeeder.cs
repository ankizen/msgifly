using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Authorization;
using Msgifly.Web.Models.Entities;

namespace Msgifly.Web.Data;

/// <summary>
/// First-run seed: permissions, an Admin role holding all of them, a Default Workspace (with its
/// lookup tables and, on an upgrade from the pre-multi-tenant single-WABA build, its WhatsApp
/// connection migrated over from the old global settings), and one superuser account from
/// configuration. Mirrors the original's DatabaseSeeder (master doc §4.3/§8.3), minus the
/// stale/broken seeders that were never actually wired in (RolesSeeder referencing a
/// non-existent model, UsersSeeder inserting a dropped column, etc. — see master doc §10 item 23).
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<int>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = services.GetRequiredService<ILogger<ApplicationDbContext>>();

        await MigrateWithRetryAsync(db, logger);

        // Permissions live as role claims on an "Admin" role (see PermissionAuthorizationHandler).
        const string adminRoleName = "Admin";
        var adminRole = await roleManager.FindByNameAsync(adminRoleName);
        if (adminRole is null)
        {
            adminRole = new IdentityRole<int>(adminRoleName);
            await roleManager.CreateAsync(adminRole);
        }

        var existingClaims = (await roleManager.GetClaimsAsync(adminRole))
            .Where(c => c.Type == PermissionAuthorizationHandler.PermissionClaimType)
            .Select(c => c.Value)
            .ToHashSet();

        foreach (var permission in Permissions.All)
        {
            if (!existingClaims.Contains(permission))
            {
                await roleManager.AddClaimAsync(adminRole, new System.Security.Claims.Claim(
                    PermissionAuthorizationHandler.PermissionClaimType, permission));
            }
        }

        await EnsureDefaultWorkspaceAsync(db, logger);

        var adminEmail = config["Seed:AdminEmail"] ?? "admin@msgifly.local";
        var adminPassword = config["Seed:AdminPassword"] ?? "Passw0rd!123";

        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser is null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FirstName = "Admin",
                LastName = "User",
                IsAdmin = true,
                Active = true,
            };

            var result = await userManager.CreateAsync(adminUser, adminPassword);
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to seed admin user: {errors}");
            }

            await userManager.AddToRoleAsync(adminUser, adminRoleName);
        }
    }

    /// <summary>
    /// Runs once, the first time the app starts against a given database. If this is a fresh
    /// install there's nothing to migrate — just create an empty Default Workspace with its
    /// default Sources/Statuses. If this is an upgrade from the pre-multi-tenant build, the old
    /// singleton WhatsApp connection and bot-behavior settings (previously stored as raw JSON
    /// under AppSettings.Group = "WhatsAppSettings" / "WhatsMarkSettings", back when those were
    /// C# classes rather than per-Workspace columns) get parsed out and copied onto this
    /// Workspace, and every pre-existing row across every workspace-scoped table gets backfilled
    /// to point at it — those rows all predate the WorkspaceId column, so EF Core's migration
    /// added it with a default value of 0, and this sweep replaces that placeholder with the
    /// Default Workspace's real id.
    /// </summary>
    private static async Task EnsureDefaultWorkspaceAsync(ApplicationDbContext db, ILogger logger)
    {
        var existing = await db.Workspaces.FirstOrDefaultAsync();
        if (existing is not null)
        {
            return;
        }

        var workspace = new Workspace { Name = "My Business" };

        var oldWaba = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Group == "WhatsAppSettings");
        if (oldWaba?.Value is not null)
        {
            try
            {
                var node = JsonNode.Parse(oldWaba.Value);
                workspace.IsAccountConnected = node?["IsAccountConnected"]?.GetValue<bool>() ?? false;
                workspace.BusinessAccountId = node?["BusinessAccountId"]?.GetValue<string>();
                workspace.AccessToken = node?["AccessToken"]?.GetValue<string>();
                workspace.DefaultPhoneNumberId = node?["DefaultPhoneNumberId"]?.GetValue<string>();
                workspace.DefaultPhoneNumber = node?["DefaultPhoneNumber"]?.GetValue<string>();
                workspace.ProfilePictureUrl = node?["ProfilePictureUrl"]?.GetValue<string>();
                workspace.ConnectionMethod = workspace.IsAccountConnected ? "manual" : null;
                logger.LogInformation("Migrated the existing single-tenant WhatsApp connection into the new Default Workspace.");

                // The Meta App identity (App id/secret, webhook verify token) moves to the new
                // global "MetaAppSettings" group under the same raw values — same shape, new name.
                var metaAppJson = new JsonObject
                {
                    ["IsWebhookConnected"] = node?["IsWebhookConnected"]?.GetValue<bool>() ?? false,
                    ["FacebookAppId"] = node?["FacebookAppId"]?.GetValue<string>(),
                    ["FacebookAppSecret"] = node?["FacebookAppSecret"]?.GetValue<string>(),
                    ["WebhookVerifyToken"] = node?["WebhookVerifyToken"]?.GetValue<string>(),
                    ["ApiVersion"] = node?["ApiVersion"]?.GetValue<string>() ?? "v21.0",
                };
                db.AppSettings.Add(new AppSetting { Group = "MetaAppSettings", Value = metaAppJson.ToJsonString() });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't parse the old WhatsAppSettings JSON during Workspace migration — starting with a disconnected Default Workspace instead.");
            }
        }

        var oldWmSettings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Group == "WhatsMarkSettings");
        if (oldWmSettings?.Value is not null)
        {
            try
            {
                var node = JsonNode.Parse(oldWmSettings.Value);
                workspace.AutoCreateLeadOnInboundMessage = node?["AutoCreateLeadOnInboundMessage"]?.GetValue<bool>() ?? true;
                workspace.DefaultLeadStatusId = node?["DefaultLeadStatusId"]?.GetValue<int?>();
                workspace.DefaultLeadSourceId = node?["DefaultLeadSourceId"]?.GetValue<int?>();
                workspace.StopBotKeywords = node?["StopBotKeywords"]?.GetValue<string>() ?? workspace.StopBotKeywords;
                workspace.RestartBotsAfterHours = node?["RestartBotsAfterHours"]?.GetValue<int?>() ?? workspace.RestartBotsAfterHours;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't parse the old WhatsMarkSettings JSON during Workspace migration — using defaults instead.");
            }
        }

        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(); // need workspace.Id before backfilling

        // Backfill BEFORE the "seed defaults if none exist" checks below, not after — every row
        // that existed prior to the WorkspaceId column has it defaulted to 0 by the migration
        // (see AddWorkspaces migration), so a legacy install's real Sources/Statuses are still
        // sitting at WorkspaceId=0 until this runs. Checking "does workspace.Id already have any
        // Sources" before this backfill always says no even when it truly already has some,
        // which used to seed a second, duplicate set on top of the ones the backfill was about
        // to bring in.
        var tables = new[]
        {
            "Contacts", "Sources", "Statuses", "Chats", "Campaigns", "WhatsappTemplates",
            "MessageBots", "TemplateBots", "CannedReplies", "Automations", "ApiKeys",
        };
        foreach (var table in tables)
        {
            // `table` is one of the fixed literals above, never external/user input, so
            // interpolating it into the SQL text (identifiers can't be parameterized) is safe —
            // only the WorkspaceId value below is a real parameter.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE [{table}] SET WorkspaceId = {{0}} WHERE WorkspaceId = 0", workspace.Id);
#pragma warning restore EF1002
        }

        logger.LogInformation("Backfilled all pre-existing rows to Default Workspace {WorkspaceId}.", workspace.Id);

        if (!await db.Sources.IgnoreQueryFilters().AnyAsync(s => s.WorkspaceId == workspace.Id))
        {
            db.Sources.AddRange(
                new Source { WorkspaceId = workspace.Id, Name = "Facebook" },
                new Source { WorkspaceId = workspace.Id, Name = "WhatsApp" },
                new Source { WorkspaceId = workspace.Id, Name = "Website" });
        }

        if (!await db.Statuses.IgnoreQueryFilters().AnyAsync(s => s.WorkspaceId == workspace.Id))
        {
            db.Statuses.AddRange(
                new Status { WorkspaceId = workspace.Id, Name = "New", Color = "#4CAF50", IsDefault = true },
                new Status { WorkspaceId = workspace.Id, Name = "In Progress", Color = "#2196F3" },
                new Status { WorkspaceId = workspace.Id, Name = "Contacted", Color = "#FFC107" },
                new Status { WorkspaceId = workspace.Id, Name = "Qualified", Color = "#9C27B0" },
                new Status { WorkspaceId = workspace.Id, Name = "Closed", Color = "#F44336" });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Docker Compose's `depends_on: condition: service_healthy` gets the container start order
    /// right, but a fresh SQL Server container can still take a few extra seconds to accept logins
    /// after its healthcheck first passes. Retry the initial connection/migration a few times
    /// rather than crash-looping the whole app on a transient cold-start race.
    /// </summary>
    private static async Task MigrateWithRetryAsync(ApplicationDbContext db, ILogger logger)
    {
        const int maxAttempts = 8;
        var delay = TimeSpan.FromSeconds(3);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex,
                    "Database not ready yet (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}s...",
                    attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay);
                delay += TimeSpan.FromSeconds(3);
            }
        }
    }
}
