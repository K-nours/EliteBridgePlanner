using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class RenameInfluenceDelta24hTo72h : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "InfluenceDelta24h",
                table: "ControlledSystems",
                newName: "InfluenceDelta72h");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "InfluenceDelta72h",
                table: "ControlledSystems",
                newName: "InfluenceDelta24h");
        }
    }
}
