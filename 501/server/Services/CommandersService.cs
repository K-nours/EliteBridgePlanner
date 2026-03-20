using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Service pour GET /api/dashboard/commanders — données depuis le cache (CommanderSnapshot / SquadronSnapshot).</summary>
public class CommandersService
{
    private readonly GuildDashboardDbContext _db;
    private static readonly TimeSpan LiveThreshold = TimeSpan.FromMinutes(5);

    public CommandersService(GuildDashboardDbContext db) => _db = db;

    public async Task<CommandersResponseDto> GetCommandersAsync(int guildId = 1, CancellationToken ct = default)
    {
        var snapshot = await _db.SquadronSnapshots
            .AsNoTracking()
            .Where(s => s.GuildId == guildId && s.Success)
            .OrderByDescending(s => s.LastSyncedAt)
            .FirstOrDefaultAsync(ct);

        var members = await _db.SquadronMembers
            .AsNoTracking()
            .Where(m => m.GuildId == guildId)
            .OrderBy(m => m.CommanderName)
            .Select(m => new CommanderDto(
                m.CommanderName,
                m.AvatarUrl,
                m.Role,
                m.LastSyncedAt))
            .ToListAsync(ct);

        var lastSyncedAt = snapshot?.LastSyncedAt;
        var dataSource = DetermineDataSource(lastSyncedAt);

        return new CommandersResponseDto(members, lastSyncedAt, dataSource);
    }

    private static string DetermineDataSource(DateTime? lastSyncedAt)
    {
        if (lastSyncedAt == null) return "cached";
        return DateTime.UtcNow - lastSyncedAt.Value <= LiveThreshold ? "live" : "cached";
    }
}
