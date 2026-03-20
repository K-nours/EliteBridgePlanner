using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSquadronMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Guilds') AND name = 'InaraSquadronId')
                ALTER TABLE [Guilds] ADD [InaraSquadronId] int NULL;
            ");

            migrationBuilder.Sql(@"
                IF OBJECT_ID('SquadronMembers', 'U') IS NULL
                BEGIN
                    CREATE TABLE [SquadronMembers] (
                        [Id] int NOT NULL IDENTITY,
                        [GuildId] int NOT NULL,
                        [CommanderName] nvarchar(450) NOT NULL,
                        [AvatarUrl] nvarchar(max) NULL,
                        [LastSyncedAt] datetime2 NULL,
                        CONSTRAINT [PK_SquadronMembers] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_SquadronMembers_Guilds_GuildId] FOREIGN KEY ([GuildId]) REFERENCES [Guilds] ([Id]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_SquadronMembers_GuildId] ON [SquadronMembers] ([GuildId]);
                    CREATE UNIQUE INDEX [IX_SquadronMembers_GuildId_CommanderName] ON [SquadronMembers] ([GuildId], [CommanderName]);
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SquadronMembers");

            migrationBuilder.DropColumn(
                name: "InaraSquadronId",
                table: "Guilds");
        }
    }
}
