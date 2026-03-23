using GuildDashboard.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Enrichissement coordonnées galactiques via EDSM (api-v1/systems?showCoordinates=1).
/// Séparé du flux Inara, non bloquant. Peut être lancé après import réussi.
/// </summary>
public class EdsmCoordsEnrichmentService
{
    private readonly GuildDashboardDbContext _db;
    private readonly EdsmApiService _edsm;
    private readonly ILogger<EdsmCoordsEnrichmentService> _log;

    private const int BatchSize = 50;
    private const int DelayBetweenBatchesMs = 400;

    public EdsmCoordsEnrichmentService(
        GuildDashboardDbContext db,
        EdsmApiService edsm,
        ILogger<EdsmCoordsEnrichmentService> log)
    {
        _db = db;
        _edsm = edsm;
        _log = log;
    }

    /// <summary>
    /// Enrichit les GuildSystems avec les coordonnées EDSM. Mode batché.
    /// Ne requête EDSM que pour les systèmes n'ayant pas encore de coords en base.
    /// </summary>
    /// <param name="onProgress">(processed, total, status).</param>
    public async Task<EdsmCoordsEnrichmentResult> EnrichAfterImportAsync(
        int guildId,
        IReadOnlyList<string> systemNames,
        Action<int, int, string>? onProgress = null,
        CancellationToken ct = default)
    {
        if (systemNames.Count == 0)
            return new EdsmCoordsEnrichmentResult(0, 0, null);

        var distinctNames = systemNames.Distinct().ToList();
        var alreadyHaveCoords = await _db.GuildSystems
            .AsNoTracking()
            .Where(s => s.GuildId == guildId && distinctNames.Contains(s.Name) && s.CoordsX != null && s.CoordsY != null && s.CoordsZ != null)
            .Select(s => s.Name)
            .ToListAsync(ct);
        var toFetch = distinctNames.Except(alreadyHaveCoords).ToList();
        if (toFetch.Count == 0)
        {
            _log.LogInformation("[EdsmCoords] Tous les systèmes ont déjà des coordonnées, skip");
            return new EdsmCoordsEnrichmentResult(alreadyHaveCoords.Count, distinctNames.Count, null);
        }

        var total = toFetch.Count;
        var enriched = 0;
        Exception? lastEx = null;
        var batches = toFetch.Chunk(BatchSize).ToList();
        var processed = 0;

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i].ToList();
            if (i > 0)
                await Task.Delay(DelayBetweenBatchesMs, ct);

            onProgress?.Invoke(processed, total, "requête EDSM");

            try
            {
                var coordsData = await _edsm.GetSystemsCoordsBatchAsync(batch, ct);
                processed += batch.Count;

                onProgress?.Invoke(processed, total, "mise à jour");

                var guildSystemsByName = await _db.GuildSystems
                    .Where(s => s.GuildId == guildId && batch.Contains(s.Name))
                    .ToListAsync(ct);
                var byName = guildSystemsByName.ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);

                foreach (var (systemName, (x, y, z)) in coordsData)
                {
                    if (byName.TryGetValue(systemName, out var gs))
                    {
                        gs.CoordsX = x;
                        gs.CoordsY = y;
                        gs.CoordsZ = z;
                        enriched++;
                    }
                }
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                _log.LogWarning(ex, "[EdsmCoords] Erreur batch {Index}/{Total}: {Message}", i + 1, batches.Count, ex.Message);
            }
        }

        var error = lastEx?.Message;
        var totalWithCoords = enriched + alreadyHaveCoords.Count;
        return new EdsmCoordsEnrichmentResult(totalWithCoords, distinctNames.Count, error);
    }
}

public record EdsmCoordsEnrichmentResult(int EnrichedCount, int TotalCount, string? Error);
