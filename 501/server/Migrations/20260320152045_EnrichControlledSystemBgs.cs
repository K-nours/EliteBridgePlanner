using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class EnrichControlledSystemBgs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InfluenceDelta24h",
                table: "ControlledSystems",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExpansionCandidate",
                table: "ControlledSystems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsThreatened",
                table: "ControlledSystems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdated",
                table: "ControlledSystems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "ControlledSystems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InfluenceDelta24h",
                table: "ControlledSystems");

            migrationBuilder.DropColumn(
                name: "IsExpansionCandidate",
                table: "ControlledSystems");

            migrationBuilder.DropColumn(
                name: "IsThreatened",
                table: "ControlledSystems");

            migrationBuilder.DropColumn(
                name: "LastUpdated",
                table: "ControlledSystems");

            migrationBuilder.DropColumn(
                name: "State",
                table: "ControlledSystems");
        }
    }
}
