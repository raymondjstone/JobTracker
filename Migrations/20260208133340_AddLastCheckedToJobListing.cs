using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddLastCheckedToJobListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastChecked",
                table: "JobListings",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastChecked",
                table: "JobListings");
        }
    }
}
