using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>SquadronSyncService — appelle Inara, stocke dans CommanderSnapshot (SquadronMembers) et SquadronSnapshot.</summary>
/// <remarks>
/// IMPORTANT — Résolution squadron STATIQUE (temporaire) :
/// Le squadron Inara est actuellement résolu via Squadron:InaraSquadronId dans appsettings
/// ou Guild.InaraSquadronId en DB. Cette logique devra être remplacée par une résolution
/// dynamique liée à l'authentification Frontier / identité guilde.
/// TODO: Replace static InaraSquadronId with dynamic squadron resolution once Frontier authentication is implemented.
/// TODO: Replace Inara roster scraping with a reliable data source (Frontier API or managed roster). Voir docs/INTEGRATION-INARA.md.
/// </remarks>
public class SquadronSyncService
{
    private readonly GuildDashboardDbContext _db;
    private readonly InaraClient _inara;
    private readonly ILogger<SquadronSyncService> _logger;

    public SquadronSyncService(GuildDashboardDbContext db, InaraClient inara, ILogger<SquadronSyncService> logger)
    {
        _db = db;
        _inara = inara;
        _logger = logger;
    }

    /// <summary>Synchronise les membres du squadron pour un guild.</summary>
    public async Task<SquadronSyncResult> SyncAsync(int guildId = 1, CancellationToken ct = default)
    {
        var guild = await _db.Guilds.AsNoTracking().FirstOrDefaultAsync(g => g.Id == guildId, ct);
        var squadronId = await _inara.GetSquadronIdAsync(guild?.InaraSquadronId, guild?.InaraFactionId, ct);

        if (squadronId == null)
        {
            _logger.LogWarning("Squadron sync skipped: InaraSquadronId not configured. Set Squadron:InaraSquadronId in appsettings.");
            return SquadronSyncResult.Failure("InaraSquadronId non configuré. Définir Squadron:InaraSquadronId dans appsettings.Development.json.");
        }

        _logger.LogInformation("Squadron sync started: InaraSquadronId={SquadronId}, GuildId={GuildId}", squadronId, guildId);

        var members = await _inara.GetSquadronMembersAsync(squadronId.Value, ct);

        if (members.Count == 0)
        {
            _logger.LogWarning("Squadron sync: no members fetched for squadron {SquadronId} (roster privé ou erreur Inara)", squadronId);
            return SquadronSyncResult.Failure("Aucun membre récupéré (roster Inara privé ou indisponible).");
        }

        var existing = await _db.SquadronMembers
            .Where(m => m.GuildId == guildId)
            .ToListAsync(ct);
        var byName = existing.ToDictionary(m => m.CommanderName, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;

        foreach (var m in members)
        {
            if (byName.TryGetValue(m.Name, out var member))
            {
                member.AvatarUrl = m.AvatarUrl ?? member.AvatarUrl;
                member.Role = m.Role ?? member.Role;
                member.LastSyncedAt = now;
            }
            else
            {
                _db.SquadronMembers.Add(new SquadronMember
                {
                    GuildId = guildId,
                    CommanderName = m.Name,
                    AvatarUrl = m.AvatarUrl,
                    Role = m.Role,
                    LastSyncedAt = now
                });
            }
        }

        var toRemove = byName.Keys.Except(members.Select(x => x.Name), StringComparer.OrdinalIgnoreCase).ToList();
        if (toRemove.Count > 0)
        {
            var toDelete = await _db.SquadronMembers
                .Where(sm => sm.GuildId == guildId && toRemove.Contains(sm.CommanderName))
                .ToListAsync(ct);
            _db.SquadronMembers.RemoveRange(toDelete);
        }

        _db.SquadronSnapshots.Add(new SquadronSnapshot
        {
            GuildId = guildId,
            LastSyncedAt = now,
            Success = true,
            MembersCount = members.Count
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Squadron sync completed: InaraSquadronId={SquadronId}, membersFetched={Fetched}, membersStored={Stored}, lastSyncedAt={LastSync}",
            squadronId, members.Count, members.Count, now.ToString("o"));

        return new SquadronSyncResult(members.Count);
    }
}
