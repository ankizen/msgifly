using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Msgifly.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationsAndApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    KeyPrefix = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ScopesCsv = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Automations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TriggerType = table.Column<int>(type: "int", nullable: false),
                    TriggerConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExecutionCount = table.Column<int>(type: "int", nullable: false),
                    LastExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Automations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutomationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AutomationId = table.Column<int>(type: "int", nullable: false),
                    ContactId = table.Column<int>(type: "int", nullable: true),
                    TriggerEvent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StepsExecutedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationLogs_Automations_AutomationId",
                        column: x => x.AutomationId,
                        principalTable: "Automations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AutomationLogs_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AutomationSteps",
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
                    table.PrimaryKey("PK_AutomationSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationSteps_AutomationSteps_ParentStepId",
                        column: x => x.ParentStepId,
                        principalTable: "AutomationSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationSteps_Automations_AutomationId",
                        column: x => x.AutomationId,
                        principalTable: "Automations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationLogs_AutomationId_CreatedAt",
                table: "AutomationLogs",
                columns: new[] { "AutomationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationLogs_ContactId",
                table: "AutomationLogs",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Automations_TriggerType_IsActive",
                table: "Automations",
                columns: new[] { "TriggerType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSteps_AutomationId_ParentStepId_Branch_Position",
                table: "AutomationSteps",
                columns: new[] { "AutomationId", "ParentStepId", "Branch", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSteps_ParentStepId",
                table: "AutomationSteps",
                column: "ParentStepId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "AutomationLogs");

            migrationBuilder.DropTable(
                name: "AutomationSteps");

            migrationBuilder.DropTable(
                name: "Automations");
        }
    }
}
