using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Authorization;
using Msgifly.Web.Data;
using Msgifly.Web.Hubs;
using Msgifly.Web.Jobs;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Services.Automations;
using Msgifly.Web.Services.Email;
using Msgifly.Web.Services.Email.Providers;
using Msgifly.Web.Services.EmailAutomations;
using Msgifly.Web.Services.EmailSequences;
using Msgifly.Web.Services.Groups;
using Msgifly.Web.Services.LeadAds;
using Msgifly.Web.Services.Settings;
using Msgifly.Web.Services.Tracking;
using Msgifly.Web.Services.WhatsApp;
using Msgifly.Web.Services.Workspaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Overrides the default factory to project ApplicationUser.IsAdmin into the "is_admin" claim.
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, ApplicationUserClaimsPrincipalFactory>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/Login";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    // Registered by name, so PermissionPolicyProvider's fallback finds it here instead of
    // treating "MasterAdminOnly" as a permission string — deliberately NOT the same as an
    // "api_key.*" permission, which a role could be granted and would bypass the intent of
    // restricting this to the is_admin superuser flag specifically (see ApiKeysController).
    options.AddPolicy("MasterAdminOnly", policy => policy.RequireClaim(PermissionAuthorizationHandler.IsAdminClaimType, "true"));
});

// Second scheme alongside Identity's cookie scheme — the public /api/v1/* surface authenticates
// machine callers via `Authorization: Bearer <api key>` instead of a browser session. Adding a
// scheme this way doesn't touch the cookie scheme Identity already registered as the default.
builder.Services.AddAuthentication()
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", options => { });

builder.Services.AddScoped<ISettingsService, SettingsService>();

// AsyncLocal-backed, so it stays consistent across a request/job's whole async flow without
// depending on HttpContext (needed for Hangfire background jobs, which have none) — see
// ICurrentWorkspaceAccessor's doc comment for the full reasoning.
builder.Services.AddSingleton<ICurrentWorkspaceAccessor, CurrentWorkspaceAccessor>();

builder.Services.AddHttpClient("GraphApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("AutomationWebhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("EmailAutomationWebhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("EmailProvider", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient("Coolify", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<EmbeddedSignupService>();
builder.Services.AddScoped<MetaLeadAdsService>();
builder.Services.AddScoped<AutomationEngine>();
builder.Services.AddScoped<ContactGroupResolver>();
builder.Services.AddScoped<IEmailSender, EmailSenderService>();
builder.Services.AddScoped<EmailMergeTagRenderer>();
builder.Services.AddScoped<EmailAudienceResolver>();
builder.Services.AddScoped<IEmailProviderHandler, SmtpProviderHandler>();
builder.Services.AddScoped<IEmailProviderHandler, BrevoProviderHandler>();
builder.Services.AddScoped<IEmailProviderHandler, SendGridProviderHandler>();
builder.Services.AddScoped<IEmailProviderHandler, MailgunProviderHandler>();
builder.Services.AddScoped<IEmailProviderHandler, AmazonSesProviderHandler>();
builder.Services.AddScoped<IEmailProviderHandler, PostmarkProviderHandler>();
builder.Services.AddScoped<IEmailProviderHandler, SparkPostProviderHandler>();
builder.Services.AddScoped<IEmailProviderHandler, NetcoreProviderHandler>();
builder.Services.AddScoped<IEmailProviderHandler, ElasticMailProviderHandler>();
builder.Services.AddScoped<IEmailProviderHandler, Smtp2GoProviderHandler>();
builder.Services.AddScoped<IEmailProviderHandler, CloudflareProviderHandler>();
builder.Services.AddScoped<EmailProviderHandlerFactory>();
builder.Services.AddScoped<EmailAutomationEngine>();
builder.Services.AddScoped<EmailSequenceService>();
builder.Services.AddScoped<LeadAdsSyncJob>();
builder.Services.AddScoped<TrackingDomainVerificationService>();
builder.Services.AddScoped<CoolifyDomainService>();
builder.Services.AddScoped<TrackingDomainVerificationJob>();
builder.Services.AddSignalR();

// MCP tools need this to read the calling API key's scopes off the request's ClaimsPrincipal —
// not registered anywhere else in this app, which has authenticated cookie/API-key access via
// the MVC/API controller pipeline instead.
builder.Services.AddHttpContextAccessor();

// Exposes create_template/create_automation/send_template_message etc. (Services/Mcp/*) as MCP
// tools over Streamable HTTP at /mcp — auth is the same "ApiKey" scheme /api/v1/* already uses,
// wired via RequireAuthorization below, not anything MCP-specific.
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString));
builder.Services.AddHangfireServer();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DbSeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// TLS is terminated at Coolify's reverse proxy (Traefik), not here — the app itself is only
// ever addressed over plain HTTP inside the container network. Trust the proxy's forwarded
// headers so cookies/auth see the real external scheme, and skip UseHttpsRedirection() (the
// proxy owns HTTP->HTTPS enforcement once a real TLS domain is configured for this app).
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
// The proxy's container IP isn't static, and it's only reachable on the internal Docker
// network anyway (this container is never addressed directly from the internet) — clearing
// these lets ASP.NET Core trust it without pinning a specific address.
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseStaticFiles();
app.UseRouting();

// Sets a cookie-based default/fallback workspace before authentication runs. ApiKeyAuthentication
// Handler overwrites this with the authoritative value once it identifies which key (and
// therefore which workspace) the request belongs to; the WhatsApp webhook controller does the
// same from the inbound payload. Both run after this and take precedence over the default.
app.UseMiddleware<WorkspaceResolutionMiddleware>();

app.UseAuthentication();

// After authentication (needs context.User populated) and before authorization — locks a
// workspace-assigned staff user's request to their one Workspace, overriding the cookie-based
// default above. See WorkspaceUserScopeMiddleware's own doc comment for how this fits alongside
// the other two overrides mentioned above it.
app.UseMiddleware<WorkspaceUserScopeMiddleware>();

app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthFilter()],
});

RecurringJob.AddOrUpdate<CampaignDispatchJob>(
    "process-scheduled-campaigns",
    job => job.ProcessScheduledCampaignsAsync(),
    Cron.Minutely());

RecurringJob.AddOrUpdate<LeadAdsSyncJob>(
    "sync-lead-ads",
    job => job.SyncAllWorkspacesAsync(),
    Cron.Minutely());

RecurringJob.AddOrUpdate<TrackingDomainVerificationJob>(
    "verify-tracking-domains",
    job => job.VerifyAllAsync(),
    Cron.Hourly());

RecurringJob.AddOrUpdate<EmailCampaignDispatchJob>(
    "process-scheduled-email-campaigns",
    job => job.ProcessScheduledCampaignsAsync(),
    Cron.Minutely());

RecurringJob.AddOrUpdate<EmailSequenceDispatchJob>(
    "process-email-sequences",
    job => job.ProcessDueAsync(),
    Cron.Minutely());

app.MapHub<ChatHub>("/hubs/chat");

// Same API-key scheme as /api/v1/* — no browser cookie here, so this is the only scheme allowed;
// each tool method then checks its own specific scope (see Services/Mcp/McpScopeGuard.cs).
app.MapMcp("/mcp").RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "ApiKey" });

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
