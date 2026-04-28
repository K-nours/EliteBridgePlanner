using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class DropEddnRawMessagesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EddnRawMessages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EddnRawMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GatewayTimestamp = table.Column<System.DateTime>(type: "datetime2", nullable: true),
                    MessageJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedAt = table.Column<System.DateTime>(type: "datetime2", nullable: false),
                    SchemaRef = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    SourceSoftware = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceUploader = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StationName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SystemName = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EddnRawMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(name: "IX_EddnRawMessages_ReceivedAt", table: "EddnRawMessages", column: "ReceivedAt");
            migrationBuilder.CreateIndex(name: "IX_EddnRawMessages_SchemaRef", table: "EddnRawMessages", column: "SchemaRef");
        }
    }
}
