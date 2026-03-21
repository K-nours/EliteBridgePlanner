using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Service pour les données du dashboard (faction, squadron, CMDRs, profil Frontier).</summary>
public class DashboardService
{
    private readonly GuildDashboardDbContext _db;
    private readonly CommandersService _commanders;
    private readonly FrontierUserService _frontierUser;

    public DashboardService(GuildDashboardDbContext db, CommandersService commanders, FrontierUserService frontierUser)
    {
        _db = db;
        _commanders = commanders;
        _frontierUser = frontierUser;
    }

    public async Task<DashboardResponseDto> GetDashboardAsync(string? commanderName, int guildId = 1, CancellationToken ct = default)
    {
        var guild = await _db.Guilds.AsNoTracking().FirstOrDefaultAsync(g => g.Id == guildId, ct);
        var factionName = guild?.FactionName ?? "The 501st Guild";
        var squadronName = guild?.SquadronName ?? "The Heirs of the 501st";

        var commandersData = await _commanders.GetCommandersAsync(guildId, ct);
        var frontierProfile = await _frontierUser.GetProfileAsync(ct);

        var currentName = commanderName ?? frontierProfile?.CommanderName;
        var cmdrs = commandersData.Commanders
            .Select(c => new CmdrDto(c.Name, c.AvatarUrl, string.Equals(c.Name, currentName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new DashboardResponseDto(factionName, squadronName, currentName, cmdrs, frontierProfile);
    }
}
