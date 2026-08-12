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
using Msgifly.Web.Services.Bots;
using Msgifly.Web.Services.Settings;
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
builder.Services.AddAuthorization();

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
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<BotMatchingService>();
builder.Services.AddScoped<AutomationEngine>();
builder.Services.AddSignalR();

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
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthFilter()],
});

RecurringJob.AddOrUpdate<CampaignDispatchJob>(
    "process-scheduled-campaigns",
    job => job.ProcessScheduledCampaignsAsync(),
    Cron.Minutely());

app.MapHub<ChatHub>("/hubs/chat");

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
