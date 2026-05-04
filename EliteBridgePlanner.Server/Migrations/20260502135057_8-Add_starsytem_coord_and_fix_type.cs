using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EliteBridgePlanner.Server.Migrations
{
    /// <inheritdoc />
    public partial class _8Add_starsytem_coord_and_fix_type : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StarSystems_Bridges_BridgeId",
                table: "StarSystems");

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
                name: "BridgeId",
                table: "StarSystems");

            migrationBuilder.DropColumn(
                name: "PreviousSystemId",
                table: "StarSystems");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "StarSystems");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "StarSystems");

            migrationBuilder.AddColumn<float>(
                name: "X",
                table: "StarSystems",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "Y",
                table: "StarSystems",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "Z",
                table: "StarSystems",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.CreateTable(
                name: "BridgeStarSystems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BridgeId = table.Column<int>(type: "int", nullable: false),
                    StarSystemId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreviousSystemId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BridgeStarSystems", x => x.Id);
                    table.UniqueConstraint("AK_BridgeStarSystems_BridgeId_StarSystemId", x => new { x.BridgeId, x.StarSystemId });
                    table.ForeignKey(
                        name: "FK_BridgeStarSystems_BridgeStarSystems_PreviousSystemId",
                        column: x => x.PreviousSystemId,
                        principalTable: "BridgeStarSystems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BridgeStarSystems_Bridges_BridgeId",
                        column: x => x.BridgeId,
                        principalTable: "Bridges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BridgeStarSystems_StarSystems_StarSystemId",
                        column: x => x.StarSystemId,
                        principalTable: "StarSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StarSystems_Name",
                table: "StarSystems",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BridgeStarSystems_PreviousSystemId",
                table: "BridgeStarSystems",
                column: "PreviousSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_BridgeStarSystems_StarSystemId",
                table: "BridgeStarSystems",
                column: "StarSystemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BridgeStarSystems");

            migrationBuilder.DropIndex(
                name: "IX_StarSystems_Name",
                table: "StarSystems");

            migrationBuilder.DropColumn(
                name: "X",
                table: "StarSystems");

            migrationBuilder.DropColumn(
                name: "Y",
                table: "StarSystems");

            migrationBuilder.DropColumn(
                name: "Z",
                table: "StarSystems");

            migrationBuilder.AddColumn<int>(
                name: "BridgeId",
                table: "StarSystems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreviousSystemId",
                table: "StarSystems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "StarSystems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "StarSystems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_StarSystems_BridgeId",
                table: "StarSystems",
                column: "BridgeId");

            migrationBuilder.CreateIndex(
                name: "IX_StarSystems_PreviousSystemId",
                table: "StarSystems",
                column: "PreviousSystemId");

            migrationBuilder.AddForeignKey(
                name: "FK_StarSystems_Bridges_BridgeId",
                table: "StarSystems",
                column: "BridgeId",
                principalTable: "Bridges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StarSystems_StarSystems_PreviousSystemId",
                table: "StarSystems",
                column: "PreviousSystemId",
                principalTable: "StarSystems",
                principalColumn: "Id");
        }
    }
}
