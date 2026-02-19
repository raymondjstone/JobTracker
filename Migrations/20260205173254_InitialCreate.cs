using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedInUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    S1JobsUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IndeedUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WTTJUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EnableAutoRules = table.Column<bool>(type: "bit", nullable: false),
                    StopOnFirstMatch = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HistoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Company = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JobUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    ChangeSource = table.Column<int>(type: "int", nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RuleName = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Company = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JobType = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DatePosted = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Salary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRemote = table.Column<bool>(type: "bit", nullable: false),
                    Skills = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Interest = table.Column<int>(type: "int", nullable: false),
                    HasApplied = table.Column<bool>(type: "bit", nullable: false),
                    DateApplied = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApplicationStage = table.Column<int>(type: "int", nullable: false),
                    StageHistory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Suitability = table.Column<int>(type: "int", nullable: false),
                    SharedToWhatsApp = table.Column<bool>(type: "bit", nullable: false),
                    DateSharedToWhatsApp = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobListings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Field = table.Column<int>(type: "int", nullable: false),
                    Operator = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CaseSensitive = table.Column<bool>(type: "bit", nullable: false),
                    Conditions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Logic = table.Column<int>(type: "int", nullable: false),
                    SetInterest = table.Column<int>(type: "int", nullable: true),
                    SetSuitability = table.Column<int>(type: "int", nullable: true),
                    TimesTriggered = table.Column<int>(type: "int", nullable: false),
                    LastTriggered = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorSecret = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PasswordResetToken = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PasswordResetExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_UserId",
                table: "AppSettings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_JobId",
                table: "HistoryEntries",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_Timestamp",
                table: "HistoryEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_UserId",
                table: "HistoryEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_JobListings_UserId",
                table: "JobListings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_JobRules_UserId",
                table: "JobRules",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "HistoryEntries");

            migrationBuilder.DropTable(
                name: "JobListings");

            migrationBuilder.DropTable(
                name: "JobRules");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
