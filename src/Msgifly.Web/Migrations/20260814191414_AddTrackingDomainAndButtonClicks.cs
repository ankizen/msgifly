using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Msgifly.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackingDomainAndButtonClicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrackingDomain",
                table: "Workspaces",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrackingDomainCheckedAt",
                table: "Workspaces",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrackingDomainStatus",
                table: "Workspaces",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TemplateButtonClicks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DestinationUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ButtonText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ButtonIndex = table.Column<int>(type: "int", nullable: false),
                    WhatsappMessageId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ClickCount = table.Column<int>(type: "int", nullable: false),
                    FirstClickedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastClickedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateButtonClicks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateButtonClicks_Token",
                table: "TemplateButtonClicks",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemplateButtonClicks_WhatsappMessageId",
                table: "TemplateButtonClicks",
                column: "WhatsappMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateButtonClicks_WorkspaceId",
                table: "TemplateButtonClicks",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TemplateButtonClicks");

            migrationBuilder.DropColumn(
                name: "TrackingDomain",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "TrackingDomainCheckedAt",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "TrackingDomainStatus",
                table: "Workspaces");
        }
    }
}
