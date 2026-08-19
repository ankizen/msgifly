using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Msgifly.Web.Migrations
{
    /// <inheritdoc />
    public partial class MergeEmailSubscriberIntoContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmailAutomationLogs_EmailSubscribers_SubscriberId",
                table: "EmailAutomationLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailCampaignRecipients_EmailSubscribers_SubscriberId",
                table: "EmailCampaignRecipients");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailSequenceSubscribers_EmailSubscribers_SubscriberId",
                table: "EmailSequenceSubscribers");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailSubscriberLists_EmailSubscribers_SubscriberId",
                table: "EmailSubscriberLists");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailSubscriberTags_EmailSubscribers_SubscriberId",
                table: "EmailSubscriberTags");

            migrationBuilder.DropTable(
                name: "EmailSubscribers");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Contacts",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailCustomFieldsJson",
                table: "Contacts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "EmailStatus",
                table: "Contacts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_Email",
                table: "Contacts",
                column: "Email");

            migrationBuilder.AddForeignKey(
                name: "FK_EmailAutomationLogs_Contacts_SubscriberId",
                table: "EmailAutomationLogs",
                column: "SubscriberId",
                principalTable: "Contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailCampaignRecipients_Contacts_SubscriberId",
                table: "EmailCampaignRecipients",
                column: "SubscriberId",
                principalTable: "Contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailSequenceSubscribers_Contacts_SubscriberId",
                table: "EmailSequenceSubscribers",
                column: "SubscriberId",
                principalTable: "Contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailSubscriberLists_Contacts_SubscriberId",
                table: "EmailSubscriberLists",
                column: "SubscriberId",
                principalTable: "Contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailSubscriberTags_Contacts_SubscriberId",
                table: "EmailSubscriberTags",
                column: "SubscriberId",
                principalTable: "Contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmailAutomationLogs_Contacts_SubscriberId",
                table: "EmailAutomationLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailCampaignRecipients_Contacts_SubscriberId",
                table: "EmailCampaignRecipients");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailSequenceSubscribers_Contacts_SubscriberId",
                table: "EmailSequenceSubscribers");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailSubscriberLists_Contacts_SubscriberId",
                table: "EmailSubscriberLists");

            migrationBuilder.DropForeignKey(
                name: "FK_EmailSubscriberTags_Contacts_SubscriberId",
                table: "EmailSubscriberTags");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_Email",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "EmailCustomFieldsJson",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "EmailStatus",
                table: "Contacts");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Contacts",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "EmailSubscribers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContactId = table.Column<int>(type: "int", nullable: true),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false)
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

            migrationBuilder.AddForeignKey(
                name: "FK_EmailAutomationLogs_EmailSubscribers_SubscriberId",
                table: "EmailAutomationLogs",
                column: "SubscriberId",
                principalTable: "EmailSubscribers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailCampaignRecipients_EmailSubscribers_SubscriberId",
                table: "EmailCampaignRecipients",
                column: "SubscriberId",
                principalTable: "EmailSubscribers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailSequenceSubscribers_EmailSubscribers_SubscriberId",
                table: "EmailSequenceSubscribers",
                column: "SubscriberId",
                principalTable: "EmailSubscribers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailSubscriberLists_EmailSubscribers_SubscriberId",
                table: "EmailSubscriberLists",
                column: "SubscriberId",
                principalTable: "EmailSubscribers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailSubscriberTags_EmailSubscribers_SubscriberId",
                table: "EmailSubscriberTags",
                column: "SubscriberId",
                principalTable: "EmailSubscribers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
