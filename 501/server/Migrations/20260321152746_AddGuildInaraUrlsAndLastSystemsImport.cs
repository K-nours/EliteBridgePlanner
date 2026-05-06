using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildInaraUrlsAndLastSystemsImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InaraFactionPresenceUrl",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InaraSquadronUrl",
                table: "Guilds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSystemsImportAt",
                table: "Guilds",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InaraFactionPresenceUrl",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "InaraSquadronUrl",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "LastSystemsImportAt",
                table: "Guilds");
        }
    }
}
