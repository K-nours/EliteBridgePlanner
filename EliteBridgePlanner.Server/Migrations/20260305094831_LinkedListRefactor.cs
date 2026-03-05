using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EliteBridgePlanner.Server.Migrations
{
    /// <inheritdoc />
    public partial class LinkedListRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StarSystem_Bridge_Order",
                table: "StarSystems");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "StarSystems");

            migrationBuilder.AddColumn<int>(
                name: "NextSystemId",
                table: "StarSystems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousSystemId",
                table: "StarSystems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StarSystems_BridgeId",
                table: "StarSystems",
                column: "BridgeId");

            migrationBuilder.CreateIndex(
                name: "IX_StarSystems_PreviousSystemId",
                table: "StarSystems",
                column: "PreviousSystemId",
                unique: true,
                filter: "[PreviousSystemId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_StarSystems_StarSystems_PreviousSystemId",
                table: "StarSystems",
                column: "PreviousSystemId",
                principalTable: "StarSystems",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StarSystems_StarSystems_PreviousSystemId",
                table: "StarSystems");

            migrationBuilder.DropIndex(
                name: "IX_StarSystems_BridgeId",
                table: "StarSystems");

            migrationBuilder.DropIndex(
                name: "IX_StarSystems_PreviousSystemId",
                table: "StarSystems");

            migrationBuilder.DropColumn(
                name: "NextSystemId",
                table: "StarSystems");

            migrationBuilder.DropColumn(
                name: "PreviousSystemId",
                table: "StarSystems");

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "StarSystems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_StarSystem_Bridge_Order",
                table: "StarSystems",
                columns: new[] { "BridgeId", "Order" },
                unique: true);
        }
    }
}
