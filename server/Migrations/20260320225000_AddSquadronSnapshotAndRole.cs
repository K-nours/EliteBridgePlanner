using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildDashboard.Server.Migrations
{
    public partial class AddSquadronSnapshotAndRole : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SquadronMembers') AND name = 'Role')
                ALTER TABLE [SquadronMembers] ADD [Role] nvarchar(max) NULL;
            ");

            migrationBuilder.Sql(@"
                IF OBJECT_ID('SquadronSnapshots', 'U') IS NULL
                BEGIN
                    CREATE TABLE [SquadronSnapshots] (
                        [Id] int NOT NULL IDENTITY,
                        [GuildId] int NOT NULL,
                        [LastSyncedAt] datetime2 NOT NULL,
                        [Success] bit NOT NULL,
                        [MembersCount] int NULL,
                        CONSTRAINT [PK_SquadronSnapshots] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_SquadronSnapshots_Guilds_GuildId] FOREIGN KEY ([GuildId]) REFERENCES [Guilds] ([Id]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_SquadronSnapshots_GuildId] ON [SquadronSnapshots] ([GuildId]);
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SquadronSnapshots");
            migrationBuilder.DropColumn(name: "Role", table: "SquadronMembers");
        }
    }
}
