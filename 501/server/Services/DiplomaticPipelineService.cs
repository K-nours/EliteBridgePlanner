using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Calcule le pipeline diplomatique : systèmes critiques (influence guilde &lt; 5%)
/// enrichis avec la faction contrôlante EDSM.
/// </summary>
public class DiplomaticPipelineService
{
    private readonly GuildDashboardDbContext _db;
    private readonly EdsmApiService _edsm;
    private readonly ILogger<DiplomaticPipelineService> _log;

    private const decimal CriticalThreshold = 5m;

    public DiplomaticPipelineService(GuildDashboardDbContext db, EdsmApiService edsm, ILogger<DiplomaticPipelineService> log)
    {
        _db = db;
        _edsm = edsm;
        _log = log;
    }

    /// <summary>
    /// Retourne les systèmes critiques enrichis avec la faction dominante EDSM.
    /// Triés par influence croissante (les plus urgents en premier).
    /// </summary>
    public async Task<DiplomaticPipelineDto> GetPipelineAsync(int guildId, CancellationToken ct = default)
    {
        var criticalSystems = await _db.GuildSystems
            .AsNoTracking()
            .Where(s => s.GuildId == guildId && s.InfluencePercent < CriticalThreshold)
            .Select(s => new { s.Name, s.InfluencePercent })
            .OrderBy(s => s.InfluencePercent)
            .ToListAsync(ct);

        _log.LogInformation("[DiplomaticPipeline] {Count} système(s) critique(s) trouvés pour guild {GuildId}", criticalSystems.Count, guildId);

        if (criticalSystems.Count == 0)
        {
            return new DiplomaticPipelineDto { Entries = [], EdsmAvailable = true };
        }

        var systemNames = criticalSystems.Select(s => s.Name).ToList();
        var edsmData = new Dictionary<string, EdsmApiService.EdsmSystemInfo>(StringComparer.OrdinalIgnoreCase);
        var edsmAvailable = true;

        try
        {
            var result = await _edsm.GetSystemsBatchAsync(systemNames, ct);
            foreach (var kv in result)
                edsmData[kv.Key] = kv.Value;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[DiplomaticPipeline] EDSM indisponible — entrées sans faction dominante");
            edsmAvailable = false;
        }

        var entries = criticalSystems.Select(s =>
        {
            edsmData.TryGetValue(s.Name, out var info);
            return new DiplomaticPipelineEntryDto
            {
                SystemName = s.Name,
                GuildInfluencePercent = s.InfluencePercent,
                DominantFaction = info?.Faction,
                DominantFactionState = info?.FactionState,
            };
        }).ToList();

        return new DiplomaticPipelineDto
        {
            Entries = entries,
            FetchedAtUtc = DateTime.UtcNow.ToString("O"),
            EdsmAvailable = edsmAvailable,
        };
    }
}
