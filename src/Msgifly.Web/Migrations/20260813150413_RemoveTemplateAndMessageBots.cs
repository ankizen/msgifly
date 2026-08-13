using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Msgifly.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTemplateAndMessageBots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageBots");

            migrationBuilder.DropTable(
                name: "TemplateBots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageBots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AddedFromId = table.Column<int>(type: "int", nullable: true),
                    Button1Id = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Button1Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Button2Id = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Button2Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Button3Id = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Button3Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CtaButtonText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CtaButtonUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FooterText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeaderText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelType = table.Column<int>(type: "int", nullable: false),
                    ReplyText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReplyType = table.Column<int>(type: "int", nullable: false),
                    SendingCount = table.Column<int>(type: "int", nullable: false),
                    TriggersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageBots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TemplateBots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BodyParamsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FooterParamsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeaderParamsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelType = table.Column<int>(type: "int", nullable: false),
                    ReplyType = table.Column<int>(type: "int", nullable: false),
                    SendingCount = table.Column<int>(type: "int", nullable: false),
                    TemplateId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TriggersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateBots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageBots_RelType_IsActive",
                table: "MessageBots",
                columns: new[] { "RelType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageBots_WorkspaceId",
                table: "MessageBots",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateBots_RelType_IsActive",
                table: "TemplateBots",
                columns: new[] { "RelType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateBots_WorkspaceId",
                table: "TemplateBots",
                column: "WorkspaceId");
        }
    }
}
