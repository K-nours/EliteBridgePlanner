using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildSystemCoords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CoordsX",
                table: "GuildSystems",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CoordsY",
                table: "GuildSystems",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CoordsZ",
                table: "GuildSystems",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoordsX",
                table: "GuildSystems");

            migrationBuilder.DropColumn(
                name: "CoordsY",
                table: "GuildSystems");

            migrationBuilder.DropColumn(
                name: "CoordsZ",
                table: "GuildSystems");
        }
    }
}
