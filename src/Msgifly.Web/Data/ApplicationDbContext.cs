using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Models.Entities;

namespace Msgifly.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

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

        builder.Entity<Status>(e => e.Property(s => s.Color).HasMaxLength(7));

        builder.Entity<Chat>(e =>
        {
            e.Property(c => c.ReceiverId).HasMaxLength(30).IsRequired();
            e.HasIndex(c => c.ReceiverId).IsUnique();

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

        builder.Entity<WhatsappTemplate>(e => e.HasIndex(t => t.MetaTemplateId).IsUnique());

        builder.Entity<Campaign>(e => e.HasIndex(c => c.TemplateId));

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

        builder.Entity<MessageBot>(e => e.HasIndex(b => new { b.RelType, b.IsActive }));
        builder.Entity<TemplateBot>(e => e.HasIndex(b => new { b.RelType, b.IsActive }));

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

        builder.Entity<Automation>(e => e.HasIndex(a => new { a.TriggerType, a.IsActive }));

        builder.Entity<ApiKey>(e => e.HasIndex(k => k.KeyHash).IsUnique());
    }
}
