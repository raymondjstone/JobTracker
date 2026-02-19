using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddSetIsRemoteToJobRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SetIsRemote",
                table: "JobRules",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetIsRemote",
                table: "JobRules");
        }
    }
}
