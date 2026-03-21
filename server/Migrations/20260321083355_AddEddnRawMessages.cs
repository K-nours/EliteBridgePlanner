using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddEddnRawMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EddnRawMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SchemaRef = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    GatewayTimestamp = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SourceSoftware = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceUploader = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SystemName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StationName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MessageJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EddnRawMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EddnRawMessages_ReceivedAt",
                table: "EddnRawMessages",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EddnRawMessages_SchemaRef",
                table: "EddnRawMessages",
                column: "SchemaRef");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EddnRawMessages");
        }
    }
}
