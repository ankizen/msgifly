using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Msgifly.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Chats_ReceiverId",
                table: "Chats");

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "WhatsappTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "TemplateBots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "Statuses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "Sources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "MessageBots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "Contacts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "Chats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "CannedReplies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "Campaigns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "Automations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "ApiKeys",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsAccountConnected = table.Column<bool>(type: "bit", nullable: false),
                    BusinessAccountId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AccessToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultPhoneNumberId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultPhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProfilePictureUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastHealthCheckAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HealthStatusJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConnectionMethod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FacebookPageId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FacebookPageName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FacebookPageAccessToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AutoCreateLeadOnInboundMessage = table.Column<bool>(type: "bit", nullable: false),
                    DefaultLeadStatusId = table.Column<int>(type: "int", nullable: true),
                    DefaultLeadSourceId = table.Column<int>(type: "int", nullable: true),
                    StopBotKeywords = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RestartBotsAfterHours = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsappTemplates_WorkspaceId",
                table: "WhatsappTemplates",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateBots_WorkspaceId",
                table: "TemplateBots",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Statuses_WorkspaceId",
                table: "Statuses",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_WorkspaceId",
                table: "Sources",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageBots_WorkspaceId",
                table: "MessageBots",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Chats_WorkspaceId_ReceiverId",
                table: "Chats",
                columns: new[] { "WorkspaceId", "ReceiverId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CannedReplies_WorkspaceId",
                table: "CannedReplies",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_WorkspaceId",
                table: "Campaigns",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Automations_WorkspaceId",
                table: "Automations",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_WorkspaceId",
                table: "ApiKeys",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_BusinessAccountId",
                table: "Workspaces",
                column: "BusinessAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropIndex(
                name: "IX_WhatsappTemplates_WorkspaceId",
                table: "WhatsappTemplates");

            migrationBuilder.DropIndex(
                name: "IX_TemplateBots_WorkspaceId",
                table: "TemplateBots");

            migrationBuilder.DropIndex(
                name: "IX_Statuses_WorkspaceId",
                table: "Statuses");

            migrationBuilder.DropIndex(
                name: "IX_Sources_WorkspaceId",
                table: "Sources");

            migrationBuilder.DropIndex(
                name: "IX_MessageBots_WorkspaceId",
                table: "MessageBots");

            migrationBuilder.DropIndex(
                name: "IX_Chats_WorkspaceId_ReceiverId",
                table: "Chats");

            migrationBuilder.DropIndex(
                name: "IX_CannedReplies_WorkspaceId",
                table: "CannedReplies");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_WorkspaceId",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Automations_WorkspaceId",
                table: "Automations");

            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_WorkspaceId",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "WhatsappTemplates");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "TemplateBots");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Statuses");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "MessageBots");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Chats");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "CannedReplies");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Automations");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "ApiKeys");

            migrationBuilder.CreateIndex(
                name: "IX_Chats_ReceiverId",
                table: "Chats",
                column: "ReceiverId",
                unique: true);
        }
    }
}
