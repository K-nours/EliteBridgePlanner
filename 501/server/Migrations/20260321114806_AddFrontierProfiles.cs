using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFrontierProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FrontierProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FrontierCustomerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CommanderName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SquadronName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSystemName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShipName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GuildId = table.Column<int>(type: "int", nullable: true),
                    LastFetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrontierProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FrontierProfiles_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FrontierProfiles_FrontierCustomerId",
                table: "FrontierProfiles",
                column: "FrontierCustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FrontierProfiles_GuildId",
                table: "FrontierProfiles",
                column: "GuildId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FrontierProfiles");
        }
    }
}
