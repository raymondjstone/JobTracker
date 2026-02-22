using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddSmtpSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SmtpFromEmail",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SmtpFromName",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SmtpHost",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SmtpPassword",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SmtpPort",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SmtpUsername",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SmtpFromEmail",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "SmtpFromName",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "SmtpHost",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "SmtpPassword",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "SmtpPort",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "SmtpUsername",
                table: "AppSettings");
        }
    }
}
