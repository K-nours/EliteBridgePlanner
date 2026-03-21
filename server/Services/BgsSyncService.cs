using GuildDashboard.Server.Data;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Synchronise les données BGS réelles (Inara scraping) vers le cache local (ControlledSystem).
/// Flux : Inara presence page → mapping → DB → GET /api/guild/systems → UI.
/// </summary>
/// <remarks>
/// Source : Inara (page minorfaction-presence). EliteBGS abandonné (timeouts systématiques).
/// Nécessite Guild.InaraFactionId configuré en base. InfluenceDelta24h, IsControlled, IsThreatened non disponibles Inara → null/false.
/// </remarks>
public class BgsSyncService
{
    private readonly GuildDashboardDbContext _db;
    private readonly InaraFactionService _inaraFaction;
    private readonly ILogger<BgsSyncService> _log;

    private static readonly string[] ExpansionCompatibleStates = ["None", "Expansion", "Boom"];

    public BgsSyncService(GuildDashboardDbContext db, InaraFactionService inaraFaction, ILogger<BgsSyncService> log)
    {
        _db = db;
        _inaraFaction = inaraFaction;
        _log = log;
    }

    /// <summary>Lance une synchronisation BGS pour la guilde. Met à jour ControlledSystem avec les données réelles (Inara).</summary>
    public async Task<BgsSyncResult> SyncAsync(int guildId = 1, CancellationToken ct = default)
    {
        var guild = await _db.Guilds
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == guildId, ct);
        if (guild == null)
            return BgsSyncResult.Failed("Guilde introuvable");

        if (!guild.InaraFactionId.HasValue || guild.InaraFactionId.Value <= 0)
        {
            _log.LogWarning("[BgsSync] InaraFactionId non configuré pour guildId={GuildId}", guildId);
            return BgsSyncResult.Failed("InaraFactionId non configuré pour cette guilde");
        }

        var factionName = guild.FactionName ?? guild.Name;
        _log.LogInformation("[BgsSync] Démarrage sync guildId={GuildId} faction={FactionName} inaraFactionId={InaraFactionId}",
            guildId, factionName, guild.InaraFactionId);

        var presence = await _inaraFaction.GetFactionPresenceAsync(guild.InaraFactionId.Value, ct);
        if (presence == null || presence.Count == 0)
        {
            _log.LogWarning("[BgsSync] RAISON: Aucune donnée Inara pour faction={FactionName} id={Id}. Page inaccessible ou structure DOM modifiée.",
                factionName, guild.InaraFactionId);
            return BgsSyncResult.Failed("Aucune donnée BGS disponible (Inara : page inaccessible ou structure modifiée)");
        }

        var guildSystems = await _db.GuildSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync(ct);
        var guildSystemNames = new HashSet<string>(guildSystems.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

        _log.LogInformation("[BgsSync] Systèmes en base ({Count}): [{Names}]",
            guildSystems.Count, string.Join(", ", guildSystems.Select(s => s.Name)));
        _log.LogInformation("[BgsSync] Systèmes Inara ({Count}): [{Names}]",
            presence.Count, string.Join(", ", presence.Take(20).Select(p => p.SystemName)) + (presence.Count > 20 ? "..." : ""));

        var presenceBySystem = presence
            .Where(p => guildSystemNames.Contains(p.SystemName))
            .ToDictionary(p => p.SystemName, p => p, StringComparer.OrdinalIgnoreCase);

        var matched = presenceBySystem.Keys.ToList();
        var notFound = guildSystems.Where(gs => !presenceBySystem.ContainsKey(gs.Name)).Select(gs => gs.Name).ToList();

        if (matched.Count > 0)
            _log.LogInformation("[BgsSync] MATCH ({Count}): [{Systems}]", matched.Count, string.Join(", ", matched));
        if (notFound.Count > 0)
            _log.LogWarning("[BgsSync] NON-MATCH / ignorés ({Count}): [{Systems}] — noms non correspondants ou absents dans Inara",
                notFound.Count, string.Join(", ", notFound));

        var updated = 0;
        var updatedNames = new List<string>();
        var now = DateTime.UtcNow;

        foreach (var gs in guildSystems)
        {
            if (!presenceBySystem.TryGetValue(gs.Name, out var pres))
                continue;

            var cs = await _db.ControlledSystems
                .FirstOrDefaultAsync(c => c.GuildId == guildId && c.Name == gs.Name, ct);

            if (cs == null)
            {
                cs = new ControlledSystem
                {
                    GuildId = guildId,
                    Name = gs.Name,
                    Category = gs.Category,
                    IsHeadquarter = false,
                    IsClean = gs.IsClean,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.ControlledSystems.Add(cs);
            }

            // Valeurs réelles depuis Inara
            cs.InfluencePercent = pres.InfluencePercent;
            cs.State = pres.State;
            cs.LastUpdated = now;
            cs.UpdatedAt = now;

            cs.InfluenceDelta24h = null;

            // IsExpansionCandidate : influence >= 60% et état compatible si disponible
            cs.IsExpansionCandidate = pres.InfluencePercent >= 60
                && (string.IsNullOrEmpty(pres.State) || ExpansionCompatibleStates.Contains(pres.State, StringComparer.OrdinalIgnoreCase));

            // Inara ne fournit pas le breakdown par faction → IsControlled/IsThreatened non déterminables
            cs.IsControlled = false;
            cs.IsThreatened = false;

            cs.IsFromSeed = false;
            updated++;
            updatedNames.Add(gs.Name);
        }

        await _db.SaveChangesAsync(ct);

        var ignored = guildSystems.Count - updated;
        if (updated > 0)
        {
            _log.LogInformation("[BgsSync] RÉSULTAT: updated={Updated} ignorés={Ignored} systems=[{Systems}]",
                updated, ignored, string.Join(", ", updatedNames));
        }
        else
        {
            _log.LogWarning("[BgsSync] RÉSULTAT: 0 système mis à jour. Ignorés={Ignored}. RAISON: {Reason}",
                ignored,
                notFound.Count == guildSystems.Count
                    ? "Aucun nom de système en base ne correspond à Inara (noms différents ou faction sans présence dans ces systèmes)"
                    : "Vérifier les logs Inara ci-dessus (parse, structure HTML)");
        }

        return BgsSyncResult.FromSuccess(updated);
    }
}

public record BgsSyncResult(bool IsSuccess, int UpdatedCount, string? ErrorMessage)
{
    public static BgsSyncResult FromSuccess(int count) => new(true, count, null);
    public static BgsSyncResult Failed(string msg) => new(false, 0, msg);
}
