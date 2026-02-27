using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddImapSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImapHost",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ImapPort",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 993);

            migrationBuilder.AddColumn<bool>(
                name: "ImapUseSsl",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ImapUsername",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImapPassword",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImapFolder",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "INBOX");

            migrationBuilder.AddColumn<bool>(
                name: "EmailCheckEnabled",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EmailCheckAutoUpdateStage",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailCheckParseJobAlerts",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ImapHost", table: "AppSettings");
            migrationBuilder.DropColumn(name: "ImapPort", table: "AppSettings");
            migrationBuilder.DropColumn(name: "ImapUseSsl", table: "AppSettings");
            migrationBuilder.DropColumn(name: "ImapUsername", table: "AppSettings");
            migrationBuilder.DropColumn(name: "ImapPassword", table: "AppSettings");
            migrationBuilder.DropColumn(name: "ImapFolder", table: "AppSettings");
            migrationBuilder.DropColumn(name: "EmailCheckEnabled", table: "AppSettings");
            migrationBuilder.DropColumn(name: "EmailCheckAutoUpdateStage", table: "AppSettings");
            migrationBuilder.DropColumn(name: "EmailCheckParseJobAlerts", table: "AppSettings");
        }
    }
}
