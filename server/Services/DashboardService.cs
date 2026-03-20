using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Service pour les données du dashboard (faction, squadron). Les CMDRs sont servis par CommandersService.</summary>
public class DashboardService
{
    private readonly GuildDashboardDbContext _db;

    public DashboardService(GuildDashboardDbContext db) => _db = db;

    public async Task<DashboardResponseDto> GetDashboardAsync(string? commanderName, int guildId = 1, CancellationToken ct = default)
    {
        var guild = await _db.Guilds.AsNoTracking().FirstOrDefaultAsync(g => g.Id == guildId, ct);
        var factionName = guild?.FactionName ?? "The 501st Guild";
        var squadronName = guild?.SquadronName ?? "The Heirs of the 501st";
        return new DashboardResponseDto(factionName, squadronName, commanderName, []);
    }
}
