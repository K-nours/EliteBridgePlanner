using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EliteBridgePlanner.Server.Migrations
{
    /// <inheritdoc />
    public partial class simplify_starssytem_link : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StarSystems_PreviousSystemId",
                table: "StarSystems");

            migrationBuilder.DropColumn(
                name: "NextSystemId",
                table: "StarSystems");

            migrationBuilder.CreateIndex(
                name: "IX_StarSystems_PreviousSystemId",
                table: "StarSystems",
                column: "PreviousSystemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StarSystems_PreviousSystemId",
                table: "StarSystems");

            migrationBuilder.AddColumn<int>(
                name: "NextSystemId",
                table: "StarSystems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StarSystems_PreviousSystemId",
                table: "StarSystems",
                column: "PreviousSystemId",
                unique: true,
                filter: "[PreviousSystemId] IS NOT NULL");
        }
    }
}
