using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Service pour GET /api/dashboard/commanders — données depuis le cache (CommanderSnapshot / SquadronSnapshot).</summary>
public class CommandersService
{
    private readonly GuildDashboardDbContext _db;
    private readonly ILogger<CommandersService> _logger;
    private static readonly TimeSpan LiveThreshold = TimeSpan.FromMinutes(5);

    public CommandersService(GuildDashboardDbContext db, ILogger<CommandersService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CommandersResponseDto> GetCommandersAsync(int guildId = 1, CancellationToken ct = default)
    {
        _logger.LogInformation("Commanders GET: guildId={GuildId} (filtre: SquadronMembers.GuildId == guildId)", guildId);

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
                m.LastSyncedAt,
                m.InaraUrl))
            .ToListAsync(ct);

        var lastSyncedAt = snapshot?.LastSyncedAt;
        var dataSource = DetermineDataSource(lastSyncedAt);

        _logger.LogInformation(
            "Commanders GET result: guildId={GuildId} count={Count} names={Names} lastSyncedAt={LastSync} dataSource={DataSource}",
            guildId, members.Count, string.Join(", ", members.Select(m => m.Name)), lastSyncedAt?.ToString("o"), dataSource);

        return new CommandersResponseDto(members, lastSyncedAt, dataSource);
    }

    private static string DetermineDataSource(DateTime? lastSyncedAt)
    {
        if (lastSyncedAt == null) return "cached";
        return DateTime.UtcNow - lastSyncedAt.Value <= LiveThreshold ? "live" : "cached";
    }
}
