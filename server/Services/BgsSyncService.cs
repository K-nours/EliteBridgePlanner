// Feature archived – no reliable external data source for Faction → Systems → Influence %.
// Conserver pour R&D futur. Voir docs/GUILD-SYSTEMS.md § Raison de clôture.

using GuildDashboard.Server.Data;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Synchronise les données BGS via EDSM (enrichissement) vers le cache local (ControlledSystem).
/// Flux : GuildSystem (base) → EDSM /api-v1/systems (batch) → ControlledSystem → GET /api/guild/systems → UI.
/// </summary>
/// <remarks>
/// Source : EDSM API. Données obtenues : faction contrôlante, factionState, IsControlled.
/// NON fourni par EDSM : InfluencePercent, InfluenceDelta24h, IsThreatened, IsExpansionCandidate, stations.
/// Nécessite Guild.FactionName. Voir docs/INTEGRATION-EDSM.md et docs/GUILD-SYSTEMS.md.
/// </remarks>
public class BgsSyncService
{
    private readonly GuildDashboardDbContext _db;
    private readonly EdsmApiService _edsm;
    private readonly ILogger<BgsSyncService> _log;

    public BgsSyncService(GuildDashboardDbContext db, EdsmApiService edsm, ILogger<BgsSyncService> log)
    {
        _db = db;
        _edsm = edsm;
        _log = log;
    }

    /// <summary>Lance une synchronisation BGS pour la guilde. Met à jour ControlledSystem avec State, IsControlled, LastUpdated depuis EDSM.</summary>
    public async Task<BgsSyncResult> SyncAsync(int guildId = 1, CancellationToken ct = default)
    {
        var guild = await _db.Guilds
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == guildId, ct);
        if (guild == null)
            return BgsSyncResult.Failed("Guilde introuvable");

        // Nettoyage des influences corrompues (> 100) avant resync
        await CleanupCorruptedInfluenceAsync(guildId, ct);

        var factionName = guild.FactionName ?? guild.Name;
        if (string.IsNullOrWhiteSpace(factionName))
        {
            _log.LogWarning("[BgsSync] FactionName non configuré pour guildId={GuildId}", guildId);
            return BgsSyncResult.Failed("FactionName non configuré pour cette guilde");
        }

        var guildSystems = await _db.GuildSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync(ct);
        if (guildSystems.Count == 0)
        {
            _log.LogWarning("[BgsSync] Aucun système en base pour guildId={GuildId}", guildId);
            return BgsSyncResult.Failed("Aucun système à synchroniser (ajouter des GuildSystem)");
        }

        var systemNames = guildSystems.Select(s => s.Name).Distinct().ToList();
        _log.LogInformation("[BgsSync] Démarrage sync guildId={GuildId} faction={FactionName} systems={Count}",
            guildId, factionName, systemNames.Count);

        IReadOnlyDictionary<string, EdsmApiService.EdsmSystemInfo> edsmData;
        try
        {
            edsmData = await _edsm.GetSystemsBatchAsync(systemNames, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[BgsSync] Erreur EDSM pour guildId={GuildId}", guildId);
            return BgsSyncResult.Failed("Erreur lors de l'appel EDSM : " + ex.Message);
        }

        if (edsmData.Count == 0)
        {
            _log.LogWarning("[BgsSync] Aucune donnée EDSM reçue pour les systèmes demandés");
            return BgsSyncResult.Failed("Aucune donnée EDSM reçue (systèmes inconnus ou API indisponible)");
        }

        var updated = 0;
        var updatedNames = new List<string>();
        var now = DateTime.UtcNow;

        foreach (var gs in guildSystems)
        {
            if (!edsmData.TryGetValue(gs.Name, out var info))
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
                    InfluencePercent = gs.InfluencePercent,
                    IsHeadquarter = false,
                    IsClean = gs.IsClean,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.ControlledSystems.Add(cs);
            }

            cs.State = string.IsNullOrWhiteSpace(info.FactionState) ? null : info.FactionState.Trim();
            cs.IsControlled = string.Equals(info.Faction?.Trim(), factionName.Trim(), StringComparison.OrdinalIgnoreCase);
            cs.LastUpdated = now;
            cs.UpdatedAt = now;

            cs.InfluenceDelta24h = null;
            cs.IsThreatened = false;
            cs.IsExpansionCandidate = false;

            cs.IsFromSeed = false;
            updated++;
            updatedNames.Add(gs.Name);
        }

        await _db.SaveChangesAsync(ct);

        var notFound = systemNames.Count - updated;
        if (notFound > 0)
            _log.LogWarning("[BgsSync] Systèmes non trouvés dans EDSM ({Count})", notFound);

        if (updated > 0)
            _log.LogInformation("[BgsSync] RÉSULTAT: updated={Updated} systems=[{Systems}]", updated, string.Join(", ", updatedNames));

        return BgsSyncResult.FromSuccess(updated);
    }

    private async Task CleanupCorruptedInfluenceAsync(int guildId, CancellationToken ct)
    {
        var corruptGuild = await _db.GuildSystems
            .Where(s => s.GuildId == guildId && s.InfluencePercent > 100)
            .Select(s => new { s.Id, s.Name, s.InfluencePercent })
            .ToListAsync(ct);
        var corruptControlled = await _db.ControlledSystems
            .Where(c => c.GuildId == guildId && c.InfluencePercent > 100)
            .Select(c => new { c.Id, c.Name, c.InfluencePercent })
            .ToListAsync(ct);

        var totalFixed = 0;
        var names = new List<string>();

        foreach (var g in corruptGuild)
        {
            await _db.GuildSystems.Where(s => s.Id == g.Id).ExecuteUpdateAsync(
                s => s.SetProperty(x => x.InfluencePercent, 0m), ct);
            totalFixed++;
            names.Add($"{g.Name}({g.InfluencePercent}%)");
        }
        foreach (var c in corruptControlled)
        {
            await _db.ControlledSystems.Where(x => x.Id == c.Id).ExecuteUpdateAsync(
                s => s.SetProperty(x => x.InfluencePercent, 0m), ct);
            totalFixed++;
            if (!names.Contains($"{c.Name}({c.InfluencePercent}%)"))
                names.Add($"{c.Name}({c.InfluencePercent}%)");
        }

        if (totalFixed > 0)
            _log.LogInformation("[BgsSync] Nettoyage influences corrompues: {Count} ligne(s) corrigée(s), systèmes: {Names}",
                totalFixed, string.Join(", ", names));
    }
}

public record BgsSyncResult(bool IsSuccess, int UpdatedCount, string? ErrorMessage)
{
    public static BgsSyncResult FromSuccess(int count) => new(true, count, null);
    public static BgsSyncResult Failed(string msg) => new(false, 0, msg);
}
