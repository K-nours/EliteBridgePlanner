using GuildDashboard.Server.Data;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Synchronise les données BGS réelles (Elite BGS API) vers le cache local (ControlledSystem).
/// Flux : Elite BGS → mapping → DB → GET /api/guild/systems → UI.
/// </summary>
/// <remarks>
/// Utilise UNIQUEMENT les propriétés de la Guild en base : FactionName (Elite BGS), InaraFactionId (non utilisé ici).
/// Le frontend ne fournit que guildId ; le backend décide de la source (Guild.FactionName).
/// InfluenceDelta24h n'est pas fourni par Elite BGS → toujours null après sync.
/// IsControlled/IsThreatened = false si l'API systems ne renvoie pas les factions.
/// </remarks>
public class BgsSyncService
{
    private readonly GuildDashboardDbContext _db;
    private readonly EliteBgsApiService _eliteBgs;
    private readonly ILogger<BgsSyncService> _log;

    private static readonly string[] ExpansionCompatibleStates = ["None", "Expansion", "Boom"];

    public BgsSyncService(GuildDashboardDbContext db, EliteBgsApiService eliteBgs, ILogger<BgsSyncService> log)
    {
        _db = db;
        _eliteBgs = eliteBgs;
        _log = log;
    }

    /// <summary>Lance une synchronisation BGS pour la guilde. Met à jour ControlledSystem avec les données réelles.</summary>
    /// <returns>Nombre de systèmes mis à jour, ou -1 en cas d'erreur.</returns>
    public async Task<BgsSyncResult> SyncAsync(int guildId = 1, CancellationToken ct = default)
    {
        var guild = await _db.Guilds
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == guildId, ct);
        if (guild == null)
            return BgsSyncResult.Failed("Guilde introuvable");

        // Source unique : propriétés Guild en base. Jamais d'identifiant de faction envoyé par le frontend.
        var factionName = guild.FactionName ?? guild.Name;
        if (string.IsNullOrWhiteSpace(factionName))
            return BgsSyncResult.Failed("Nom de faction non configuré");

        _log.LogInformation("[BgsSync] Démarrage sync guildId={GuildId} faction={FactionName}", guildId, factionName);

        var presence = await _eliteBgs.GetFactionPresenceAsync(factionName, ct);
        if (presence == null || presence.Count == 0)
        {
            _log.LogWarning("[BgsSync] RAISON: Aucune donnée Elite BGS pour faction={FactionName}. Faction introuvable ou API vide.", factionName);
            return BgsSyncResult.Failed("Aucune donnée BGS disponible pour cette faction (faction introuvable ou API vide)");
        }

        var guildSystems = await _db.GuildSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync(ct);
        var guildSystemNames = new HashSet<string>(guildSystems.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

        _log.LogInformation("[BgsSync] Systèmes en base ({Count}): [{Names}]",
            guildSystems.Count, string.Join(", ", guildSystems.Select(s => s.Name)));
        _log.LogInformation("[BgsSync] Systèmes Elite BGS ({Count}): [{Names}]",
            presence.Count, string.Join(", ", presence.Select(p => p.SystemName)));

        var presenceBySystem = presence
            .Where(p => guildSystemNames.Contains(p.SystemName))
            .ToDictionary(p => p.SystemName, p => p, StringComparer.OrdinalIgnoreCase);

        var matched = presenceBySystem.Keys.ToList();
        var notFound = guildSystems.Where(gs => !presenceBySystem.ContainsKey(gs.Name)).Select(gs => gs.Name).ToList();

        if (matched.Count > 0)
            _log.LogInformation("[BgsSync] MATCH ({Count}): [{Systems}]", matched.Count, string.Join(", ", matched));
        if (notFound.Count > 0)
            _log.LogWarning("[BgsSync] NON-MATCH / ignorés ({Count}): [{Systems}] — noms non correspondants ou absents dans Elite BGS",
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

            // Valeurs réelles depuis Elite BGS
            cs.InfluencePercent = pres.InfluencePercent;
            cs.State = pres.State;
            cs.LastUpdated = now;
            cs.UpdatedAt = now;

            // InfluenceDelta24h : Elite BGS ne fournit pas d'historique → null (pas de valeur seed trompeuse)
            cs.InfluenceDelta24h = null;

            // IsExpansionCandidate : calculé (influence > 60% et état compatible)
            cs.IsExpansionCandidate = pres.InfluencePercent >= 60
                && (string.IsNullOrEmpty(pres.State) || ExpansionCompatibleStates.Contains(pres.State, StringComparer.OrdinalIgnoreCase));

            // IsControlled, IsThreatened : nécessitent les factions du système
            var systemFactions = await _eliteBgs.GetSystemFactionsAsync(gs.Name, ct);
            if (systemFactions != null && systemFactions.Count > 0)
            {
                var sorted = systemFactions.OrderByDescending(x => x.InfluencePercent).ToList();
                cs.IsControlled = string.Equals(sorted[0].FactionName, factionName, StringComparison.OrdinalIgnoreCase);
                cs.IsThreatened = sorted.Count > 1 && (sorted[0].InfluencePercent - sorted[1].InfluencePercent) < 10;
            }
            else
            {
                cs.IsControlled = false;
                cs.IsThreatened = false;
            }

            cs.IsFromSeed = false;
            updated++;
            updatedNames.Add(gs.Name);

            // Rate limit pour éviter de surcharger l'API
            if (updated < presenceBySystem.Count)
                await Task.Delay(500, ct);
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
                    ? "Aucun nom de système en base ne correspond à Elite BGS (noms différents ou faction sans présence dans ces systèmes)"
                    : "Vérifier les logs Elite BGS ci-dessus (parse, structure JSON)");
        }

        return BgsSyncResult.FromSuccess(updated);
    }
}

public record BgsSyncResult(bool IsSuccess, int UpdatedCount, string? ErrorMessage)
{
    public static BgsSyncResult FromSuccess(int count) => new(true, count, null);
    public static BgsSyncResult Failed(string msg) => new(false, 0, msg);
}
