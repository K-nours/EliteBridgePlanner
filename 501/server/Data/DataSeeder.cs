using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Data;

/// <summary>Seed minimal — 1 Guild. Les systèmes sont chargés par GuildSystemsSeedLoader (guild-systems.seed.json).</summary>
public class DataSeeder
{
    private readonly GuildDashboardDbContext _db;

    public DataSeeder(GuildDashboardDbContext db) => _db = db;

    public async Task SeedAsync()
    {
        if (await _db.Guilds.AnyAsync())
            return;

        var guild = new Guild
        {
            Name = "The 501st Guild",
            DisplayName = "The 501st Guild",
            SquadronName = "The Heirs of the 501st",
            FactionName = "The 501st Guild",
            InaraFactionId = 78866
            // InaraSquadronId : configurer dans appsettings Squadron:InaraSquadronId ou via la DB
            // Ex: 4926 pour 501st Legion German Garrison (roster public)
        };
        _db.Guilds.Add(guild);
        await _db.SaveChangesAsync();

        // GuildSystems et ControlledSystems : chargés par GuildSystemsSeedLoader (guild-systems.seed.json)
    }
}
