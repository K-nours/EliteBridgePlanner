using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Data;

/// <summary>Seed minimal pour Guild Systems — 1 Guild + quelques ControlledSystems de test.</summary>
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

        var now = DateTime.UtcNow;
        var systems = new[]
        {
            new GuildSystem { GuildId = guild.Id, Name = "Hip 4332", Category = "Origine", InfluencePercent = 55.7m, IsClean = true },
            new GuildSystem { GuildId = guild.Id, Name = "Mayang", Category = "Guild", InfluencePercent = 65.9m, IsClean = true },
            new GuildSystem { GuildId = guild.Id, Name = "Hip 4794", Category = "Guild", InfluencePercent = 55.7m, IsClean = true },
            new GuildSystem { GuildId = guild.Id, Name = "Sabines", Category = "Guild", InfluencePercent = 55.7m, IsClean = false },
            new GuildSystem { GuildId = guild.Id, Name = "Achuar", Category = "Guild", InfluencePercent = 1.5m, IsClean = false },
            new GuildSystem { GuildId = guild.Id, Name = "Reticuli", Category = "Guild", InfluencePercent = 2.2m, IsClean = false },
        };
        _db.GuildSystems.AddRange(systems);
        await _db.SaveChangesAsync();

        // Données seed/démo — non représentatives du jeu. IsControlled/IsThreatened/IsExpansionCandidate = valeurs arbitraires.
        var controlled = new[]
        {
            new ControlledSystem { GuildId = guild.Id, Name = "Hip 4332", InfluencePercent = 55.7m, InfluenceDelta24h = 0.5m, State = "Boom", IsControlled = true, IsThreatened = false, IsExpansionCandidate = false, IsHeadquarter = false, IsFromSeed = true, IsClean = true, Category = "Origine", LastUpdated = now, CreatedAt = now, UpdatedAt = now },
            new ControlledSystem { GuildId = guild.Id, Name = "Mayang", InfluencePercent = 65.9m, InfluenceDelta24h = -0.2m, State = "None", IsControlled = true, IsThreatened = false, IsExpansionCandidate = false, IsHeadquarter = true, IsFromSeed = true, IsClean = true, Category = "Guild", LastUpdated = now, CreatedAt = now, UpdatedAt = now },
            new ControlledSystem { GuildId = guild.Id, Name = "Hip 4794", InfluencePercent = 55.7m, InfluenceDelta24h = 1.2m, State = "Expansion", IsControlled = true, IsThreatened = false, IsExpansionCandidate = true, IsClean = true, IsFromSeed = true, Category = "Guild", LastUpdated = now, CreatedAt = now, UpdatedAt = now },
            new ControlledSystem { GuildId = guild.Id, Name = "Sabines", InfluencePercent = 55.7m, InfluenceDelta24h = -0.8m, State = "War", IsControlled = false, IsThreatened = true, IsExpansionCandidate = false, IsClean = false, IsFromSeed = true, Category = "Guild", LastUpdated = now, CreatedAt = now, UpdatedAt = now },
            new ControlledSystem { GuildId = guild.Id, Name = "Achuar", InfluencePercent = 1.5m, InfluenceDelta24h = -0.5m, State = "Bust", IsControlled = false, IsThreatened = true, IsExpansionCandidate = false, IsClean = false, IsFromSeed = true, Category = "Guild", LastUpdated = now, CreatedAt = now, UpdatedAt = now },
            new ControlledSystem { GuildId = guild.Id, Name = "Reticuli", InfluencePercent = 2.2m, InfluenceDelta24h = 0.1m, State = "Civil unrest", IsControlled = false, IsThreatened = true, IsExpansionCandidate = false, IsClean = false, IsFromSeed = true, Category = "Guild", LastUpdated = now, CreatedAt = now, UpdatedAt = now },
        };
        _db.ControlledSystems.AddRange(controlled);
        await _db.SaveChangesAsync();
    }
}
