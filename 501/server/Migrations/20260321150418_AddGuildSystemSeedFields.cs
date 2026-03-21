using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildSystemSeedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "GuildSystems",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "Allegiance",
                table: "GuildSystems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FactionCount",
                table: "GuildSystems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Government",
                table: "GuildSystems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastUpdatedText",
                table: "GuildSystems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Population",
                table: "GuildSystems",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Power",
                table: "GuildSystems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StationCount",
                table: "GuildSystems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_GuildSystems_GuildId_Name",
                table: "GuildSystems",
                columns: new[] { "GuildId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GuildSystems_GuildId_Name",
                table: "GuildSystems");

            migrationBuilder.DropColumn(
                name: "Allegiance",
                table: "GuildSystems");

            migrationBuilder.DropColumn(
                name: "FactionCount",
                table: "GuildSystems");

            migrationBuilder.DropColumn(
                name: "Government",
                table: "GuildSystems");

            migrationBuilder.DropColumn(
                name: "LastUpdatedText",
                table: "GuildSystems");

            migrationBuilder.DropColumn(
                name: "Population",
                table: "GuildSystems");

            migrationBuilder.DropColumn(
                name: "Power",
                table: "GuildSystems");

            migrationBuilder.DropColumn(
                name: "StationCount",
                table: "GuildSystems");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "GuildSystems",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
