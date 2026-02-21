using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineSettingsAndFollowUpDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FollowUpDate",
                table: "JobListings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GhostedDays",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NoReplyDays",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StaleDays",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FollowUpDate",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "GhostedDays",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "NoReplyDays",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "StaleDays",
                table: "AppSettings");
        }
    }
}
