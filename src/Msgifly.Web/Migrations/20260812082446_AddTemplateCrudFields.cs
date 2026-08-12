using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Msgifly.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateCrudFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsappTemplates_MetaTemplateId",
                table: "WhatsappTemplates");

            migrationBuilder.AlterColumn<string>(
                name: "MetaTemplateId",
                table: "WhatsappTemplates",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "HeaderMediaUrl",
                table: "WhatsappTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "WhatsappTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SampleValuesJson",
                table: "WhatsappTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionError",
                table: "WhatsappTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsappTemplates_MetaTemplateId",
                table: "WhatsappTemplates",
                column: "MetaTemplateId",
                unique: true,
                filter: "[MetaTemplateId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsappTemplates_MetaTemplateId",
                table: "WhatsappTemplates");

            migrationBuilder.DropColumn(
                name: "HeaderMediaUrl",
                table: "WhatsappTemplates");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "WhatsappTemplates");

            migrationBuilder.DropColumn(
                name: "SampleValuesJson",
                table: "WhatsappTemplates");

            migrationBuilder.DropColumn(
                name: "SubmissionError",
                table: "WhatsappTemplates");

            migrationBuilder.AlterColumn<string>(
                name: "MetaTemplateId",
                table: "WhatsappTemplates",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsappTemplates_MetaTemplateId",
                table: "WhatsappTemplates",
                column: "MetaTemplateId",
                unique: true);
        }
    }
}
