// Feature archived – no reliable external data source for Faction → Systems → Influence %.
// Conserver pour R&D futur. Voir docs/GUILD-SYSTEMS.md § Raison de clôture.

using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Service dédié au panneau Guild Systems.</summary>
/// <remarks>
/// DataSource : seed = données seedées/démo (DataSeeder). cached = données issues d'une sync BGS (EDSM, etc.).
/// Jamais "live" sans sync fraîche vérifiée.
/// </remarks>
public class GuildSystemsService
{
    private readonly GuildDashboardDbContext _db;

    public GuildSystemsService(GuildDashboardDbContext db) => _db = db;

    public async Task<GuildSystemsResponseDto> GetSystemsAsync(int guildId = 1)
    {
        var guildExists = await _db.Guilds.AnyAsync(g => g.Id == guildId);
        if (!guildExists)
            return new GuildSystemsResponseDto([], [], [], "seed");

        var guildSystems = await _db.GuildSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync();
        var controlled = await _db.ControlledSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync();
        var controlledByName = controlled.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var origin = new List<GuildSystemBgsDto>();
        var headquarter = new List<GuildSystemBgsDto>();
        var others = new List<GuildSystemBgsDto>();
        var anyFromSync = false;

        foreach (var gs in guildSystems)
        {
            var cs = controlledByName.GetValueOrDefault(gs.Name);
            var dto = ToDto(gs, cs);
            var isOrigin = string.Equals(gs.Category, "Origine", StringComparison.OrdinalIgnoreCase);
            var isHq = cs?.IsHeadquarter == true;

            if (cs != null && !cs.IsFromSeed)
                anyFromSync = true;

            if (isOrigin)
                origin.Add(dto);
            if (isHq)
                headquarter.Add(dto);
            if (!isOrigin && !isHq)
                others.Add(dto);
        }

        others = others
            .OrderByDescending(s => s.IsThreatened || s.IsExpansionCandidate)
            .ThenByDescending(s => s.InfluencePercent)
            .ThenBy(s => s.Name)
            .ToList();

        var dataSource = anyFromSync ? "cached" : "seed";
        return new GuildSystemsResponseDto(origin, headquarter, others, dataSource);
    }

    /// <summary>Toggle HQ : si le système n'est pas HQ, le définit comme HQ (et retire les autres). S'il est déjà HQ, retire le statut.</summary>
    public async Task<bool> ToggleHeadquarterAsync(int guildSystemId, int guildId, CancellationToken ct = default)
    {
        var gs = await _db.GuildSystems
            .FirstOrDefaultAsync(s => s.Id == guildSystemId && s.GuildId == guildId, ct);
        if (gs == null)
            return false;

        var cs = await _db.ControlledSystems
            .FirstOrDefaultAsync(c => c.GuildId == guildId && c.Name == gs.Name, ct);

        if (cs == null)
        {
            cs = new ControlledSystem
            {
                GuildId = guildId,
                Name = gs.Name,
                InfluencePercent = gs.InfluencePercent,
                IsClean = gs.IsClean,
                Category = gs.Category,
                IsControlled = false,
                IsHeadquarter = true,
                IsFromSeed = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _db.ControlledSystems.Add(cs);
        }
        else
        {
            var wasHq = cs.IsHeadquarter;
            cs.IsHeadquarter = !wasHq;
            cs.UpdatedAt = DateTime.UtcNow;

            if (!wasHq)
            {
                var othersHq = await _db.ControlledSystems
                    .Where(c => c.GuildId == guildId && c.Id != cs.Id && c.IsHeadquarter)
                    .ToListAsync(ct);
                foreach (var o in othersHq)
                {
                    o.IsHeadquarter = false;
                    o.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static GuildSystemBgsDto ToDto(GuildSystem gs, ControlledSystem? cs)
    {
        // InfluenceDelta24h : n'afficher que si source réelle (sync). Jamais de valeur seed trompeuse.
        decimal? delta = (cs != null && !cs.IsFromSeed && cs.InfluenceDelta24h != null) ? cs.InfluenceDelta24h : null;

        var isFromSeed = cs?.IsFromSeed ?? true;
        return new GuildSystemBgsDto(
            gs.Id,
            gs.Name,
            cs?.InfluencePercent ?? gs.InfluencePercent,
            delta,
            cs?.State,
            cs?.IsThreatened ?? false,
            cs?.IsExpansionCandidate ?? false,
            cs?.IsHeadquarter ?? false,
            cs?.IsClean ?? gs.IsClean,
            gs.Category,
            cs?.LastUpdated,
            isFromSeed
        );
    }
}
