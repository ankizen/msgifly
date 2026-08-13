using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Msgifly.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryTrackingDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Clicked",
                table: "ChatMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ClickedButtonText",
                table: "ChatMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "ChatMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FailedAt",
                table: "ChatMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt",
                table: "ChatMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAt",
                table: "ChatMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateName",
                table: "ChatMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Clicked",
                table: "CampaignDetails",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ClickedButtonText",
                table: "CampaignDetails",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "CampaignDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FailedAt",
                table: "CampaignDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt",
                table: "CampaignDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepliedAt",
                table: "CampaignDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAt",
                table: "CampaignDetails",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Clicked",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ClickedButtonText",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "FailedAt",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "TemplateName",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "Clicked",
                table: "CampaignDetails");

            migrationBuilder.DropColumn(
                name: "ClickedButtonText",
                table: "CampaignDetails");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "CampaignDetails");

            migrationBuilder.DropColumn(
                name: "FailedAt",
                table: "CampaignDetails");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "CampaignDetails");

            migrationBuilder.DropColumn(
                name: "RepliedAt",
                table: "CampaignDetails");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "CampaignDetails");
        }
    }
}
