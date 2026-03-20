using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Service dédié au panneau Guild Systems.</summary>
public class GuildSystemsService
{
    private readonly GuildDashboardDbContext _db;

    public GuildSystemsService(GuildDashboardDbContext db) => _db = db;

    public async Task<GuildSystemsResponseDto> GetSystemsAsync(int guildId = 1)
    {
        var guildExists = await _db.Guilds.AnyAsync(g => g.Id == guildId);
        if (!guildExists)
            return new GuildSystemsResponseDto([], [], []);

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

        foreach (var gs in guildSystems)
        {
            var cs = controlledByName.GetValueOrDefault(gs.Name);
            var dto = ToDto(gs, cs);
            var isOrigin = string.Equals(gs.Category, "Origine", StringComparison.OrdinalIgnoreCase);

            if (isOrigin)
                origin.Add(dto);
            else if (cs?.IsHeadquarter == true)
                headquarter.Add(dto);
            else
                others.Add(dto);
        }

        others = others
            .OrderByDescending(s => s.IsThreatened || s.IsExpansionCandidate)
            .ThenByDescending(s => s.InfluencePercent)
            .ThenBy(s => s.Name)
            .ToList();

        return new GuildSystemsResponseDto(origin, headquarter, others);
    }

    private static GuildSystemBgsDto ToDto(GuildSystem gs, ControlledSystem? cs)
    {
        var category = cs?.IsHeadquarter == true ? "Headquarter" : gs.Category;
        return new GuildSystemBgsDto(
            gs.Id,
            gs.Name,
            cs?.InfluencePercent ?? gs.InfluencePercent,
            cs?.InfluenceDelta24h,
            cs?.State,
            cs?.IsThreatened ?? false,
            cs?.IsExpansionCandidate ?? false,
            cs?.IsClean ?? gs.IsClean,
            category,
            cs?.LastUpdated
        );
    }
}
