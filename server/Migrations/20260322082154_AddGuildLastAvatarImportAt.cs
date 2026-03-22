using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildLastAvatarImportAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastAvatarImportAt",
                table: "Guilds",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAvatarImportAt",
                table: "Guilds");
        }
    }
}
