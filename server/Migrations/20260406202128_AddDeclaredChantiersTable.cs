using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDeclaredChantiersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeclaredChantiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GuildId = table.Column<int>(type: "int", nullable: false),
                    CmdrName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SystemName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    StationName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    MarketId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SystemNameKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    StationNameKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    DeclaredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConstructionResourcesJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeclaredChantiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeclaredChantiers_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeclaredChantiers_GuildId",
                table: "DeclaredChantiers",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_DeclaredChantiers_GuildId_MarketId",
                table: "DeclaredChantiers",
                columns: new[] { "GuildId", "MarketId" },
                unique: true,
                filter: "[MarketId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeclaredChantiers_GuildId_SystemNameKey_StationNameKey",
                table: "DeclaredChantiers",
                columns: new[] { "GuildId", "SystemNameKey", "StationNameKey" },
                unique: true,
                filter: "[MarketId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeclaredChantiers");
        }
    }
}
