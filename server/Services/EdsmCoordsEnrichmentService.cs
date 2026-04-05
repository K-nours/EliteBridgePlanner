using GuildDashboard.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Enrichissement coordonnées galactiques + classe spectrale principale via EDSM
/// (<c>showCoordinates=1</c> + <c>showInformation=1</c>, champ <c>primaryStar.type</c>).
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
    /// Enrichit les GuildSystems : coords et/ou PrimaryStarClass. Mode batché.
    /// Ne requête EDSM que pour les systèmes sans coords complètes ou sans classe stellaire.
    /// </summary>
    public async Task<EdsmCoordsEnrichmentResult> EnrichAfterImportAsync(
        int guildId,
        IReadOnlyList<string> systemNames,
        Action<int, int, string>? onProgress = null,
        CancellationToken ct = default)
    {
        if (systemNames.Count == 0)
            return new EdsmCoordsEnrichmentResult(0, 0, null);

        var distinctNames = systemNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var completeNames = await _db.GuildSystems
            .AsNoTracking()
            .Where(s =>
                s.GuildId == guildId
                && distinctNames.Contains(s.Name)
                && s.CoordsX != null
                && s.CoordsY != null
                && s.CoordsZ != null
                && s.PrimaryStarClass != null
                && s.PrimaryStarClass != "")
            .Select(s => s.Name)
            .ToListAsync(ct);

        var complete = new HashSet<string>(completeNames, StringComparer.OrdinalIgnoreCase);
        var toFetch = distinctNames.Where(n => !complete.Contains(n)).ToList();

        if (toFetch.Count == 0)
        {
            _log.LogInformation("[EdsmCoords] Tous les systèmes ont coords + PrimaryStarClass, skip");
            await LogStarClassCoverageAsync(guildId, distinctNames, ct);
            var n = await CountWithCoordsAsync(guildId, distinctNames, ct);
            return new EdsmCoordsEnrichmentResult(n, distinctNames.Count, null);
        }

        var total = toFetch.Count;
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
                var data = await _edsm.GetSystemsCoordsBatchAsync(batch, ct);
                processed += batch.Count;

                onProgress?.Invoke(processed, total, "mise à jour");

                var guildSystemsByName = await _db.GuildSystems
                    .Where(s => s.GuildId == guildId && batch.Contains(s.Name))
                    .ToListAsync(ct);
                var byName = guildSystemsByName.ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);

                foreach (var kv in data)
                {
                    var systemName = kv.Key;
                    var row = kv.Value;
                    if (!byName.TryGetValue(systemName, out var gs))
                        continue;

                    if (row.X.HasValue && row.Y.HasValue && row.Z.HasValue)
                    {
                        if (gs.CoordsX == null || gs.CoordsY == null || gs.CoordsZ == null)
                        {
                            gs.CoordsX = row.X;
                            gs.CoordsY = row.Y;
                            gs.CoordsZ = row.Z;
                        }
                    }

                    var norm = EdsmStarClassNormalizer.Normalize(row.PrimaryStarTypeRaw);
                    if (norm != null)
                        gs.PrimaryStarClass = norm;
                }

                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                _log.LogWarning(ex, "[EdsmCoords] Erreur batch {Index}/{Total}: {Message}", i + 1, batches.Count, ex.Message);
            }
        }

        await LogStarClassCoverageAsync(guildId, distinctNames, ct);

        var error = lastEx?.Message;
        var finalWithCoords = await CountWithCoordsAsync(guildId, distinctNames, ct);
        return new EdsmCoordsEnrichmentResult(finalWithCoords, distinctNames.Count, error);
    }

    private async Task<int> CountWithCoordsAsync(int guildId, IReadOnlyList<string> distinctNames, CancellationToken ct)
    {
        return await _db.GuildSystems
            .AsNoTracking()
            .CountAsync(s =>
                s.GuildId == guildId
                && distinctNames.Contains(s.Name)
                && s.CoordsX != null
                && s.CoordsY != null
                && s.CoordsZ != null,
                ct);
    }

    private async Task LogStarClassCoverageAsync(int guildId, IReadOnlyList<string> distinctNames, CancellationToken ct)
    {
        var total = await _db.GuildSystems
            .AsNoTracking()
            .CountAsync(s => s.GuildId == guildId && distinctNames.Contains(s.Name), ct);
        var withStar = await _db.GuildSystems
            .AsNoTracking()
            .CountAsync(s =>
                s.GuildId == guildId
                && distinctNames.Contains(s.Name)
                && s.PrimaryStarClass != null
                && s.PrimaryStarClass != "",
                ct);
        var pct = total == 0 ? 0 : 100.0 * withStar / total;
        _log.LogInformation("[StarClass] total={Total} withPrimaryStarClass={With} pct={Pct:F2}", total, withStar, pct);
    }
}

public record EdsmCoordsEnrichmentResult(int EnrichedCount, int TotalCount, string? Error);
