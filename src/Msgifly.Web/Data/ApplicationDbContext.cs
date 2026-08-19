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

    /// <summary>
    /// SQL Server's datetime2 columns carry no timezone info, so EF Core always materializes
    /// DateTime reads with Kind=Unspecified — even though every DateTime this app writes is
    /// DateTime.UtcNow. System.Text.Json then serializes an Unspecified-kind value WITHOUT a "Z"
    /// suffix, and a browser's `new Date(...)` treats a suffix-less ISO string as LOCAL time, not
    /// UTC — silently double-shifting anything read back from the DB and rendered client-side
    /// (first surfaced as chat message timestamps looking right on send, the freshly-constructed
    /// in-memory DateTime still Kind=Utc, but wrong after a refresh once it's round-tripped
    /// through SQL Server). Stamping Kind=Utc back on every DateTime read fixes this at the root
    /// for every entity, not just Chat.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter() : base(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    private sealed class NullableUtcDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter() : base(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
        {
        }
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
    public DbSet<TemplateButtonClick> TemplateButtonClicks => Set<TemplateButtonClick>();
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

    // --- Email Marketing (independent stack, see Models/Entities/Email*.cs) ---------------------
    // No separate "EmailSubscriber" DbSet — Contact IS the email subscriber (see Contact.EmailStatus).
    public DbSet<EmailList> EmailLists => Set<EmailList>();
    public DbSet<EmailTag> EmailTags => Set<EmailTag>();
    public DbSet<EmailSubscriberList> EmailSubscriberLists => Set<EmailSubscriberList>();
    public DbSet<EmailSubscriberTag> EmailSubscriberTags => Set<EmailSubscriberTag>();
    public DbSet<EmailCustomField> EmailCustomFields => Set<EmailCustomField>();
    public DbSet<EmailSmtpConnection> EmailSmtpConnections => Set<EmailSmtpConnection>();
    public DbSet<EmailCampaign> EmailCampaigns => Set<EmailCampaign>();
    public DbSet<EmailCampaignRecipient> EmailCampaignRecipients => Set<EmailCampaignRecipient>();
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();
    public DbSet<EmailAutomation> EmailAutomations => Set<EmailAutomation>();
    public DbSet<EmailAutomationStep> EmailAutomationSteps => Set<EmailAutomationStep>();
    public DbSet<EmailAutomationLog> EmailAutomationLogs => Set<EmailAutomationLog>();
    public DbSet<EmailSequence> EmailSequences => Set<EmailSequence>();
    public DbSet<EmailSequenceMail> EmailSequenceMails => Set<EmailSequenceMail>();
    public DbSet<EmailSequenceSubscriber> EmailSequenceSubscribers => Set<EmailSequenceSubscriber>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<LeadAdsImport> LeadAdsImports => Set<LeadAdsImport>();
    public DbSet<LeadAdsForm> LeadAdsForms => Set<LeadAdsForm>();
    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<ContactGroup> ContactGroups => Set<ContactGroup>();
    public DbSet<ContactGroupMember> ContactGroupMembers => Set<ContactGroupMember>();

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
        builder.Entity<TemplateButtonClick>().HasQueryFilter(c => c.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<CannedReply>().HasQueryFilter(r => r.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<Automation>().HasQueryFilter(a => a.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<ApiKey>().HasQueryFilter(k => k.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<LeadAdsImport>().HasQueryFilter(l => l.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<LeadAdsForm>().HasQueryFilter(f => f.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<Flow>().HasQueryFilter(f => f.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<ContactGroup>().HasQueryFilter(g => g.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<ContactGroupMember>().HasQueryFilter(m => m.Group.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<ContactNote>().HasQueryFilter(n => n.Contact.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<ChatMessage>().HasQueryFilter(m => m.Chat.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<CampaignDetail>().HasQueryFilter(d => d.Campaign.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<AutomationStep>().HasQueryFilter(s => s.Automation.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<AutomationLog>().HasQueryFilter(l => l.Automation.WorkspaceId == _workspaceAccessor.WorkspaceId);

        // Email Marketing — independent stack, same scoping pattern as above. No EmailSubscriber
        // filter: Contact already carries one (see the very first line of this block).
        builder.Entity<EmailList>().HasQueryFilter(l => l.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailTag>().HasQueryFilter(t => t.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailCustomField>().HasQueryFilter(f => f.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailSmtpConnection>().HasQueryFilter(c => c.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailCampaign>().HasQueryFilter(c => c.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailLog>().HasQueryFilter(l => l.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailAutomation>().HasQueryFilter(a => a.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailSequence>().HasQueryFilter(s => s.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailSubscriberList>().HasQueryFilter(s => s.Subscriber.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailSubscriberTag>().HasQueryFilter(s => s.Subscriber.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailCampaignRecipient>().HasQueryFilter(r => r.Campaign.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailAutomationStep>().HasQueryFilter(s => s.Automation.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailAutomationLog>().HasQueryFilter(l => l.Automation.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailSequenceMail>().HasQueryFilter(m => m.Sequence.WorkspaceId == _workspaceAccessor.WorkspaceId);
        builder.Entity<EmailSequenceSubscriber>().HasQueryFilter(s => s.Sequence.WorkspaceId == _workspaceAccessor.WorkspaceId);

        builder.Entity<Contact>(e =>
        {
            e.Property(c => c.FirstName).HasMaxLength(255).IsRequired();
            e.Property(c => c.LastName).HasMaxLength(255).IsRequired();
            e.Property(c => c.Phone).HasMaxLength(30).IsRequired();
            e.HasIndex(c => c.Phone);
            // Not unique — a Contact can exist with no email at all (WhatsApp-only lead), and two
            // Contacts sharing an email isn't a real constraint here the way phone is. Just an
            // index for Email Marketing's own lookups (audience resolution, unsubscribe-by-token).
            e.HasIndex(c => c.Email);

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

        builder.Entity<ApplicationUser>(e =>
        {
            // SetNull, not Restrict/Cascade: deleting a Workspace shouldn't cascade-delete or
            // block on the staff user(s) assigned to it — they just fall back to unscoped.
            e.HasOne(u => u.Workspace)
                .WithMany()
                .HasForeignKey(u => u.WorkspaceId)
                .OnDelete(DeleteBehavior.SetNull);
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

        builder.Entity<TemplateButtonClick>(e =>
        {
            e.HasIndex(c => c.Token).IsUnique();
            e.HasIndex(c => c.WorkspaceId);
            e.HasIndex(c => c.WhatsappMessageId);
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
        builder.Entity<LeadAdsForm>(e => e.HasIndex(f => new { f.WorkspaceId, f.FormId }).IsUnique());

        builder.Entity<Flow>(e =>
        {
            e.HasIndex(f => f.MetaFlowId).IsUnique().HasFilter("[MetaFlowId] IS NOT NULL");
            e.HasIndex(f => f.WorkspaceId);
        });

        builder.Entity<ContactGroup>(e => e.HasIndex(g => g.WorkspaceId));

        builder.Entity<ContactGroupMember>(e =>
        {
            e.HasIndex(m => new { m.GroupId, m.ContactId }).IsUnique();

            e.HasOne(m => m.Group)
                .WithMany(g => g.Members)
                .HasForeignKey(m => m.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(m => m.Contact)
                .WithMany()
                .HasForeignKey(m => m.ContactId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // --- Email Marketing (independent stack; see Models/Entities/Email*.cs) -----------------
        // No EmailSubscriber config — Contact IS the email subscriber (see the Contact block above).

        builder.Entity<EmailList>(e => e.HasIndex(l => l.WorkspaceId));
        builder.Entity<EmailTag>(e => e.HasIndex(t => t.WorkspaceId));

        builder.Entity<EmailSubscriberList>(e =>
        {
            e.HasIndex(s => new { s.SubscriberId, s.ListId }).IsUnique();

            e.HasOne(s => s.Subscriber)
                .WithMany()
                .HasForeignKey(s => s.SubscriberId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(s => s.List)
                .WithMany(l => l.Members)
                .HasForeignKey(s => s.ListId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EmailSubscriberTag>(e =>
        {
            e.HasIndex(s => new { s.SubscriberId, s.TagId }).IsUnique();

            e.HasOne(s => s.Subscriber)
                .WithMany()
                .HasForeignKey(s => s.SubscriberId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(s => s.Tag)
                .WithMany(t => t.Members)
                .HasForeignKey(s => s.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EmailCustomField>(e => e.HasIndex(f => new { f.WorkspaceId, f.Key }).IsUnique());

        builder.Entity<EmailSmtpConnection>(e => e.HasIndex(c => c.WorkspaceId));

        builder.Entity<EmailCampaign>(e => e.HasIndex(c => c.WorkspaceId));

        builder.Entity<EmailCampaignRecipient>(e =>
        {
            e.HasIndex(r => r.TrackingToken).IsUnique();
            e.HasIndex(r => r.Status);

            e.HasOne(r => r.Campaign)
                .WithMany(c => c.Recipients)
                .HasForeignKey(r => r.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.Subscriber)
                .WithMany()
                .HasForeignKey(r => r.SubscriberId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.EmailLog)
                .WithMany()
                .HasForeignKey(r => r.EmailLogId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<EmailLog>(e =>
        {
            e.HasIndex(l => new { l.CreatedAt, l.Status });
            e.HasIndex(l => l.WorkspaceId);
        });

        builder.Entity<EmailAutomation>(e =>
        {
            e.HasIndex(a => a.WorkspaceId);
            e.HasIndex(a => new { a.TriggerType, a.IsActive });
        });

        builder.Entity<EmailAutomationStep>(e =>
        {
            e.HasOne(s => s.Automation)
                .WithMany(a => a.Steps)
                .HasForeignKey(s => s.AutomationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict (not Cascade) on the self-reference — same reasoning as AutomationStep:
            // deleting the whole EmailAutomation already removes every step via the FK above in
            // one shot, so a second cascade path off ParentStepId isn't needed — and SQL Server
            // rejects multiple cascade paths to the same table anyway.
            e.HasOne(s => s.ParentStep)
                .WithMany()
                .HasForeignKey(s => s.ParentStepId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(s => new { s.AutomationId, s.ParentStepId, s.Branch, s.Position });
        });

        builder.Entity<EmailAutomationLog>(e =>
        {
            e.HasOne(l => l.Automation)
                .WithMany()
                .HasForeignKey(l => l.AutomationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.Subscriber)
                .WithMany()
                .HasForeignKey(l => l.SubscriberId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(l => new { l.AutomationId, l.CreatedAt });
        });

        builder.Entity<EmailSequence>(e =>
        {
            e.HasIndex(s => s.WorkspaceId);

            e.HasOne(s => s.AutoEnrollList)
                .WithMany()
                .HasForeignKey(s => s.AutoEnrollListId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<EmailSequenceMail>(e =>
        {
            e.HasOne(m => m.Sequence)
                .WithMany(s => s.Mails)
                .HasForeignKey(m => m.SequenceId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(m => new { m.SequenceId, m.Order });
        });

        builder.Entity<EmailSequenceSubscriber>(e =>
        {
            e.HasIndex(s => new { s.SequenceId, s.SubscriberId }).IsUnique();
            e.HasIndex(s => new { s.Status, s.NextExecutionAt });

            e.HasOne(s => s.Sequence)
                .WithMany()
                .HasForeignKey(s => s.SequenceId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(s => s.Subscriber)
                .WithMany()
                .HasForeignKey(s => s.SubscriberId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
