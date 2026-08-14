using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Msgifly.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadAdsFormPageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PageId",
                table: "LeadAdsForms",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PageId",
                table: "LeadAdsForms");
        }
    }
}
