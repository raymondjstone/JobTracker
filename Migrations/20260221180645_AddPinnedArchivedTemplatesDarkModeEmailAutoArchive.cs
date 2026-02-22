using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddPinnedArchivedTemplatesDarkModeEmailAutoArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "JobListings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "JobListings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "JobListings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AutoArchiveDays",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AutoArchiveEnabled",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CoverLetterTemplatesJson",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "DarkMode",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EmailNotificationsEnabled",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EmailOnFollowUpDue",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EmailOnStaleApplications",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "AutoArchiveDays",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "AutoArchiveEnabled",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "CoverLetterTemplatesJson",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DarkMode",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "EmailNotificationsEnabled",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "EmailOnFollowUpDue",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "EmailOnStaleApplications",
                table: "AppSettings");
        }
    }
}
