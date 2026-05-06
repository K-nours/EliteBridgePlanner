using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddControlledSystemIsHeadquarter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ControlledSystems_GuildId",
                table: "ControlledSystems");

            migrationBuilder.AddColumn<bool>(
                name: "IsHeadquarter",
                table: "ControlledSystems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ControlledSystems_GuildId",
                table: "ControlledSystems",
                column: "GuildId",
                unique: true,
                filter: "[IsHeadquarter] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ControlledSystems_GuildId",
                table: "ControlledSystems");

            migrationBuilder.DropColumn(
                name: "IsHeadquarter",
                table: "ControlledSystems");

            migrationBuilder.CreateIndex(
                name: "IX_ControlledSystems_GuildId",
                table: "ControlledSystems",
                column: "GuildId");
        }
    }
}
