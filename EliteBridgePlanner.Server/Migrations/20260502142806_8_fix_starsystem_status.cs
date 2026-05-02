using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EliteBridgePlanner.Server.Migrations
{
    /// <inheritdoc />
    public partial class _8_fix_starsystem_status : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "BridgeStarSystems");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "StarSystems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "StarSystems");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "BridgeStarSystems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }
    }
}
