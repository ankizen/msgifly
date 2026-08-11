using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Authorization;
using Msgifly.Web.Models.Entities;

namespace Msgifly.Web.Data;

/// <summary>
/// First-run seed: permissions, an Admin role holding all of them, the lookup tables every
/// Contact needs (Sources/Statuses), and one superuser account from configuration.
/// Mirrors the original's DatabaseSeeder (master doc §4.3/§8.3), minus the stale/broken seeders
/// that were never actually wired in (RolesSeeder referencing a non-existent model, UsersSeeder
/// inserting a dropped column, etc. — see master doc §10 item 23).
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

        if (!db.Sources.Any())
        {
            db.Sources.AddRange(
                new Source { Name = "Facebook" },
                new Source { Name = "WhatsApp" },
                new Source { Name = "Website" });
        }

        if (!db.Statuses.Any())
        {
            db.Statuses.AddRange(
                new Status { Name = "New", Color = "#4CAF50", IsDefault = true },
                new Status { Name = "In Progress", Color = "#2196F3" },
                new Status { Name = "Contacted", Color = "#FFC107" },
                new Status { Name = "Qualified", Color = "#9C27B0" },
                new Status { Name = "Closed", Color = "#F44336" });
        }

        await db.SaveChangesAsync();

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
