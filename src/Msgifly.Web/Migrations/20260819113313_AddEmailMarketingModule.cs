using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Msgifly.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailMarketingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailAutomations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TriggerType = table.Column<int>(type: "int", nullable: false),
                    TriggerConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAutomations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailCampaigns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FromEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SendNow = table.Column<bool>(type: "bit", nullable: false),
                    SelectAll = table.Column<bool>(type: "bit", nullable: false),
                    IncludeListIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExcludeListIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IncludeTagIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExcludeTagIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailCustomFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FieldType = table.Column<int>(type: "int", nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCustomFields", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    ToEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FromEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResponseMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailSmtpConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Password = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EnableSsl = table.Column<bool>(type: "bit", nullable: false),
                    FromEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    MaxSendsPerMinute = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSmtpConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailSubscribers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContactId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    CustomFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSubscribers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSubscribers_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmailSubscribers_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmailTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailAutomationSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AutomationId = table.Column<int>(type: "int", nullable: false),
                    ParentStepId = table.Column<int>(type: "int", nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    StepType = table.Column<int>(type: "int", nullable: false),
                    StepConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAutomationSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailAutomationSteps_EmailAutomationSteps_ParentStepId",
                        column: x => x.ParentStepId,
                        principalTable: "EmailAutomationSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmailAutomationSteps_EmailAutomations_AutomationId",
                        column: x => x.AutomationId,
                        principalTable: "EmailAutomations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AutoEnrollListId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSequences_EmailLists_AutoEnrollListId",
                        column: x => x.AutoEnrollListId,
                        principalTable: "EmailLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmailAutomationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AutomationId = table.Column<int>(type: "int", nullable: false),
                    SubscriberId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAutomationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailAutomationLogs_EmailAutomations_AutomationId",
                        column: x => x.AutomationId,
                        principalTable: "EmailAutomations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailAutomationLogs_EmailSubscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "EmailSubscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailCampaignRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignId = table.Column<int>(type: "int", nullable: false),
                    SubscriberId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EmailLogId = table.Column<int>(type: "int", nullable: true),
                    TrackingToken = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsOpened = table.Column<bool>(type: "bit", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsClicked = table.Column<bool>(type: "bit", nullable: false),
                    ClickCount = table.Column<int>(type: "int", nullable: false),
                    ClickedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsUnsubscribed = table.Column<bool>(type: "bit", nullable: false),
                    UnsubscribedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCampaignRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailCampaignRecipients_EmailCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "EmailCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailCampaignRecipients_EmailLogs_EmailLogId",
                        column: x => x.EmailLogId,
                        principalTable: "EmailLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmailCampaignRecipients_EmailSubscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "EmailSubscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailSubscriberLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscriberId = table.Column<int>(type: "int", nullable: false),
                    ListId = table.Column<int>(type: "int", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSubscriberLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSubscriberLists_EmailLists_ListId",
                        column: x => x.ListId,
                        principalTable: "EmailLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailSubscriberLists_EmailSubscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "EmailSubscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailSubscriberTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscriberId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSubscriberTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSubscriberTags_EmailSubscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "EmailSubscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailSubscriberTags_EmailTags_TagId",
                        column: x => x.TagId,
                        principalTable: "EmailTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailSequenceMails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SequenceId = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DelayAmount = table.Column<int>(type: "int", nullable: false),
                    DelayUnit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSequenceMails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSequenceMails_EmailSequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "EmailSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailSequenceSubscribers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SequenceId = table.Column<int>(type: "int", nullable: false),
                    SubscriberId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LastMailId = table.Column<int>(type: "int", nullable: true),
                    NextMailId = table.Column<int>(type: "int", nullable: true),
                    NextExecutionAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSequenceSubscribers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSequenceSubscribers_EmailSequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "EmailSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailSequenceSubscribers_EmailSubscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "EmailSubscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAutomationLogs_AutomationId_CreatedAt",
                table: "EmailAutomationLogs",
                columns: new[] { "AutomationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAutomationLogs_SubscriberId",
                table: "EmailAutomationLogs",
                column: "SubscriberId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailAutomations_TriggerType_IsActive",
                table: "EmailAutomations",
                columns: new[] { "TriggerType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAutomations_WorkspaceId",
                table: "EmailAutomations",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailAutomationSteps_AutomationId_ParentStepId_Branch_Position",
                table: "EmailAutomationSteps",
                columns: new[] { "AutomationId", "ParentStepId", "Branch", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAutomationSteps_ParentStepId",
                table: "EmailAutomationSteps",
                column: "ParentStepId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaignRecipients_CampaignId",
                table: "EmailCampaignRecipients",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaignRecipients_EmailLogId",
                table: "EmailCampaignRecipients",
                column: "EmailLogId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaignRecipients_Status",
                table: "EmailCampaignRecipients",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaignRecipients_SubscriberId",
                table: "EmailCampaignRecipients",
                column: "SubscriberId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaignRecipients_TrackingToken",
                table: "EmailCampaignRecipients",
                column: "TrackingToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaigns_WorkspaceId",
                table: "EmailCampaigns",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailCustomFields_WorkspaceId_Key",
                table: "EmailCustomFields",
                columns: new[] { "WorkspaceId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailLists_WorkspaceId",
                table: "EmailLists",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_CreatedAt_Status",
                table: "EmailLogs",
                columns: new[] { "CreatedAt", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_WorkspaceId",
                table: "EmailLogs",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSequenceMails_SequenceId_Order",
                table: "EmailSequenceMails",
                columns: new[] { "SequenceId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailSequences_AutoEnrollListId",
                table: "EmailSequences",
                column: "AutoEnrollListId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSequences_WorkspaceId",
                table: "EmailSequences",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSequenceSubscribers_SequenceId_SubscriberId",
                table: "EmailSequenceSubscribers",
                columns: new[] { "SequenceId", "SubscriberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailSequenceSubscribers_Status_NextExecutionAt",
                table: "EmailSequenceSubscribers",
                columns: new[] { "Status", "NextExecutionAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailSequenceSubscribers_SubscriberId",
                table: "EmailSequenceSubscribers",
                column: "SubscriberId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSmtpConnections_WorkspaceId",
                table: "EmailSmtpConnections",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSubscriberLists_ListId",
                table: "EmailSubscriberLists",
                column: "ListId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSubscriberLists_SubscriberId_ListId",
                table: "EmailSubscriberLists",
                columns: new[] { "SubscriberId", "ListId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailSubscribers_ContactId",
                table: "EmailSubscribers",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSubscribers_SourceId",
                table: "EmailSubscribers",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSubscribers_WorkspaceId_Email",
                table: "EmailSubscribers",
                columns: new[] { "WorkspaceId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailSubscriberTags_SubscriberId_TagId",
                table: "EmailSubscriberTags",
                columns: new[] { "SubscriberId", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailSubscriberTags_TagId",
                table: "EmailSubscriberTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTags_WorkspaceId",
                table: "EmailTags",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailAutomationLogs");

            migrationBuilder.DropTable(
                name: "EmailAutomationSteps");

            migrationBuilder.DropTable(
                name: "EmailCampaignRecipients");

            migrationBuilder.DropTable(
                name: "EmailCustomFields");

            migrationBuilder.DropTable(
                name: "EmailSequenceMails");

            migrationBuilder.DropTable(
                name: "EmailSequenceSubscribers");

            migrationBuilder.DropTable(
                name: "EmailSmtpConnections");

            migrationBuilder.DropTable(
                name: "EmailSubscriberLists");

            migrationBuilder.DropTable(
                name: "EmailSubscriberTags");

            migrationBuilder.DropTable(
                name: "EmailAutomations");

            migrationBuilder.DropTable(
                name: "EmailCampaigns");

            migrationBuilder.DropTable(
                name: "EmailLogs");

            migrationBuilder.DropTable(
                name: "EmailSequences");

            migrationBuilder.DropTable(
                name: "EmailSubscribers");

            migrationBuilder.DropTable(
                name: "EmailTags");

            migrationBuilder.DropTable(
                name: "EmailLists");
        }
    }
}
