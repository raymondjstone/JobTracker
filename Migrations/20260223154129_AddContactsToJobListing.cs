using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddContactsToJobListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AICoverLetterClosing",
                table: "JobListings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AICoverLetterOpening",
                table: "JobListings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AICoverLetterPoints",
                table: "JobListings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "AINiceToHaveSkills",
                table: "JobListings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "AIQualifications",
                table: "JobListings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "AIRequiredSkills",
                table: "JobListings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "AIResponsibilities",
                table: "JobListings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "AISummary",
                table: "JobListings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Contacts",
                table: "JobListings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdated",
                table: "JobListings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SuitabilityScore",
                table: "JobListings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FieldName",
                table: "HistoryEntries",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AICoverLetterClosing",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "AICoverLetterOpening",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "AICoverLetterPoints",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "AINiceToHaveSkills",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "AIQualifications",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "AIRequiredSkills",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "AIResponsibilities",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "AISummary",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "Contacts",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "LastUpdated",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "SuitabilityScore",
                table: "JobListings");

            migrationBuilder.DropColumn(
                name: "FieldName",
                table: "HistoryEntries");
        }
    }
}
