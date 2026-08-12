using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    // Holds the ACCESSOR (service reference), not a snapshotted value: the query filter lambdas
    // below read "_workspaceAccessor.WorkspaceId" as a property-getter member access, which EF
    // Core re-evaluates as a query parameter on every execution rather than baking in whatever
    // the value was at DbContext construction time. That matters because the current workspace
    // can become known only partway through a request — e.g. the WhatsApp webhook controller
    // resolves it from the inbound payload's WABA id, well after this DbContext (and its
    // constructor) already ran — so the filter must pick up later writes to the accessor, not
    // just what it held when this instance was built.
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentWorkspaceAccessor workspaceAccessor) : base(options)
    {
        _workspaceAccessor = workspaceAccessor;
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactNote> ContactNotes => Set<ContactNote>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<Status> Statuses => Set<Status>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignDetail> CampaignDetails => Set<CampaignDetail>();
    public DbSet<WhatsappTemplate> WhatsappTemplates => Set<WhatsappTemplate>();
    public DbSet<MessageBot> MessageBots => Set<MessageBot>();
    public DbSet<TemplateBot> TemplateBots => Set<TemplateBot>();
    public DbSet<CannedReply> CannedReplies => Set<CannedReply>();
    public DbSet<AiPrompt> AiPrompts => Set<AiPrompt>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<WebhookLog> WebhookLogs => Set<WebhookLog>();
    public DbSet<WmActivityLog> WmActivityLogs => Set<WmActivityLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Automation> Automations => Set<Automation>();
    public DbSet<AutomationStep> AutomationSteps => Set<AutomationStep>();
    public DbSet<AutomationLog> AutomationLogs => Set<AutomationLog>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<LeadAdsImport> LeadAdsImports => Set<LeadAdsImport>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // --- Multi-tenant scoping -------------------------------------------------------------
        // Every business-data entity below carries a WorkspaceId and is filtered against the
        // request/job's current workspace automatically, so no controller or query anywhere else
        // in the app needs to remember to add ".Where(x => x.WorkspaceId == ...)" itself. Child
        // entities without their own WorkspaceId column (ContactNote, ChatMessage,
        // CampaignDetail, AutomationStep, AutomationLog) are filtered through their parent
        // navigation instead — EF Core translates that into a join/exists check, not a second
        // denormalized column. ApplicationUser/Role/Permissions and the AppSetting/GeneralSettings
        // groups stay global by design: the same admin(s) manage every workspace, and Meta App
        // identity (FacebookAppId/Secret, webhook verify token) is one App shared by all of them.
        builder.Entity<Contact>().HasQueryFilter(c => c.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<Source>().HasQueryFilter(s => s.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<Status>().HasQueryFilter(s => s.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<Chat>().HasQueryFilter(c => c.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<Campaign>().HasQueryFilter(c => c.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<WhatsappTemplate>().HasQueryFilter(t => t.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<MessageBot>().HasQueryFilter(b => b.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<TemplateBot>().HasQueryFilter(b => b.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<CannedReply>().HasQueryFilter(r => r.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<Automation>().HasQueryFilter(a => a.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<ApiKey>().HasQueryFilter(k => k.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<LeadAdsImport>().HasQueryFilter(l => l.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<ContactNote>().HasQueryFilter(n => n.Contact.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<ChatMessage>().HasQueryFilter(m => m.Chat.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<CampaignDetail>().HasQueryFilter(d => d.Campaign.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<AutomationStep>().HasQueryFilter(s => s.Automation.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<AutomationLog>().HasQueryFilter(l => l.Automation.WorkspaceId == _workspaceAccessor.WorkspaceId);

        builder.Entity<Contact>(e =>
        {
            e.Property(c => c.FirstName).HasMaxLength(255).IsRequired();
            e.Property(c => c.LastName).HasMaxLength(255).IsRequired();
            e.Property(c => c.Phone).HasMaxLength(30).IsRequired();
            e.HasIndex(c => c.Phone);

            e.HasOne(c => c.Status)
                .WithMany(s => s.Contacts)
                .HasForeignKey(c => c.StatusId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.Source)
                .WithMany(s => s.Contacts)
                .HasForeignKey(c => c.SourceId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.AssignedTo)
                .WithMany(u => u.AssignedContacts)
                .HasForeignKey(c => c.AssignedToId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ContactNote>(e =>
        {
            e.HasOne(n => n.Contact)
                .WithMany(c => c.Notes)
                .HasForeignKey(n => n.ContactId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Status>(e =>
        {
            e.Property(s => s.Color).HasMaxLength(7);
            e.HasIndex(s => s.WorkspaceId);
        });

        builder.Entity<Chat>(e =>
        {
            e.Property(c => c.ReceiverId).HasMaxLength(30).IsRequired();
            // Unique per-Workspace, not globally: the same customer number can message
            // different businesses' WhatsApp numbers, each its own Chat thread.
            e.HasIndex(c => new { c.WorkspaceId, c.ReceiverId }).IsUnique();

            e.HasOne(c => c.AssignedAgent)
                .WithMany()
                .HasForeignKey(c => c.AssignedAgentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ChatMessage>(e =>
        {
            e.HasOne(m => m.Chat)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(m => m.WhatsappMessageId);
        });

        builder.Entity<WhatsappTemplate>(e =>
        {
            e.HasIndex(t => t.MetaTemplateId).IsUnique();
            e.HasIndex(t => t.WorkspaceId);
        });

        builder.Entity<Campaign>(e =>
        {
            e.HasIndex(c => c.TemplateId);
            e.HasIndex(c => c.WorkspaceId);
        });

        builder.Entity<CampaignDetail>(e =>
        {
            e.HasOne(d => d.Campaign)
                .WithMany(c => c.Details)
                .HasForeignKey(d => d.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(d => d.Contact)
                .WithMany()
                .HasForeignKey(d => d.ContactId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(d => d.WhatsappMessageId);
        });

        builder.Entity<MessageBot>(e =>
        {
            e.HasIndex(b => new { b.RelType, b.IsActive });
            e.HasIndex(b => b.WorkspaceId);
        });
        builder.Entity<TemplateBot>(e =>
        {
            e.HasIndex(b => new { b.RelType, b.IsActive });
            e.HasIndex(b => b.WorkspaceId);
        });

        builder.Entity<Source>(e => e.HasIndex(s => s.WorkspaceId));
        builder.Entity<CannedReply>(e => e.HasIndex(r => r.WorkspaceId));

        builder.Entity<EmailTemplate>(e => e.HasIndex(t => t.Slug).IsUnique());
        builder.Entity<Language>(e => e.HasIndex(l => l.Code).IsUnique());

        builder.Entity<AppSetting>(e => e.HasIndex(s => s.Group).IsUnique());

        builder.Entity<AutomationStep>(e =>
        {
            e.HasOne(s => s.Automation)
                .WithMany(a => a.Steps)
                .HasForeignKey(s => s.AutomationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict (not Cascade) on the self-reference: deleting the whole Automation already
            // removes every step via the FK above in one shot, so a second cascade path off
            // ParentStepId isn't needed — and SQL Server rejects multiple cascade paths to the
            // same table anyway.
            e.HasOne(s => s.ParentStep)
                .WithMany()
                .HasForeignKey(s => s.ParentStepId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(s => new { s.AutomationId, s.ParentStepId, s.Branch, s.Position });
        });

        builder.Entity<AutomationLog>(e =>
        {
            e.HasOne(l => l.Automation)
                .WithMany()
                .HasForeignKey(l => l.AutomationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.Contact)
                .WithMany()
                .HasForeignKey(l => l.ContactId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(l => new { l.AutomationId, l.CreatedAt });
        });

        builder.Entity<Automation>(e =>
        {
            e.HasIndex(a => new { a.TriggerType, a.IsActive });
            e.HasIndex(a => a.WorkspaceId);
        });

        builder.Entity<ApiKey>(e =>
        {
            e.HasIndex(k => k.KeyHash).IsUnique();
            e.HasIndex(k => k.WorkspaceId);
        });

        builder.Entity<Workspace>(e => e.HasIndex(w => w.BusinessAccountId));

        builder.Entity<LeadAdsImport>(e => e.HasIndex(l => new { l.WorkspaceId, l.MetaLeadId }).IsUnique());
    }
}
