using System.Text.Json;
using GuildDashboard.Server.Data;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Enrichissement post-import Inara : récupère la tendance d'influence depuis EDSM (api-system-v1/factions?showHistory=1)
/// et la stocke dans ControlledSystem.InfluenceDelta72h.
/// Fenêtre 72h (3 jours) pour aligner avec l'affichage EDSM (24h donnait souvent 0 car même tick BGS).
/// Mode batché : N requêtes en parallèle par batch (API EDSM ne supporte pas le batch multi-systèmes).
/// </summary>
public class EdsmDeltaEnrichmentService
{
    private readonly GuildDashboardDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<EdsmDeltaEnrichmentService> _log;

    private static readonly string[] AuditSystemNames = ["HIP 4332", "Mayang", "Nanapan"];
    private const int BatchSize = 20;
    private const int DelayBetweenBatchesMs = 500;

    public EdsmDeltaEnrichmentService(
        GuildDashboardDbContext db,
        IHttpClientFactory httpFactory,
        ILogger<EdsmDeltaEnrichmentService> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _log = log;
    }

    /// <summary>
    /// Après un import Inara réussi, enrichit les ControlledSystems avec InfluenceDelta72h depuis EDSM.
    /// Mode batché : BatchSize systèmes en parallèle par batch (l'API EDSM factions ne supporte qu'un système par requête).
    /// </summary>
    /// <param name="onProgress">(processed, total, status) avec status = "préparation" | "requête groupée" | "réponse reçue" | "analyse".</param>
    public async Task<EdsmEnrichmentResult> EnrichAfterImportAsync(
        int guildId,
        IReadOnlyList<string> systemNames,
        Action<int, int, string>? onProgress = null,
        CancellationToken ct = default)
    {
        const string mode = "groupé";
        if (systemNames.Count == 0)
            return new EdsmEnrichmentResult(mode, 0, 0, 0, null);

        var guild = await _db.Guilds
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == guildId, ct);
        if (guild == null)
            return new EdsmEnrichmentResult(mode, 0, 0, 0, "Guilde introuvable");

        var factionName = guild.FactionName ?? guild.Name;
        if (string.IsNullOrWhiteSpace(factionName))
        {
            _log.LogInformation("[EdsmDelta] FactionName non configuré, enrichissement ignoré");
            return new EdsmEnrichmentResult(mode, 0, 0, 0, null);
        }

        onProgress?.Invoke(0, systemNames.Count, "préparation");
        var distinctNames = systemNames.Distinct().ToList();
        var total = distinctNames.Count;
        var auditSet = new HashSet<string>(AuditSystemNames.Select(Normalize), StringComparer.OrdinalIgnoreCase);

        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Add("User-Agent", "GuildDashboard/1.0");

        var enriched = 0;
        var displayable = 0;
        var ignored = 0;
        Exception? lastEx = null;
        var batches = distinctNames.Chunk(BatchSize).ToList();
        var processed = 0;

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            if (i > 0)
                await Task.Delay(DelayBetweenBatchesMs, ct);

            onProgress?.Invoke(processed, total, "requête groupée");

            var tasks = batch.Select(name => FetchDeltaAsync(client, name, factionName, ct)).ToList();
            var results = await Task.WhenAll(tasks);
            processed += batch.Length;

            onProgress?.Invoke(processed, total, "réponse reçue");
            onProgress?.Invoke(processed, total, "analyse");

            for (var j = 0; j < batch.Length; j++)
            {
                var systemName = batch[j];
                var (deltaRaw, reason) = results[j];

                try
                {
                    decimal? rounded = null;
                    if (deltaRaw.HasValue)
                        rounded = Math.Round(deltaRaw.Value, 2, MidpointRounding.AwayFromZero);

                    if (reason != null)
                    {
                        if (auditSet.Contains(Normalize(systemName)))
                            _log.LogInformation("[EdsmDelta] {System} — absent: {Reason}", systemName, reason);
                        continue;
                    }

                    var cs = await _db.ControlledSystems
                        .FirstOrDefaultAsync(c => c.GuildId == guildId && c.Name == systemName, ct);
                    if (cs != null && !cs.IsFromSeed)
                    {
                        cs.InfluenceDelta72h = deltaRaw;
                        cs.UpdatedAt = DateTime.UtcNow;
                        await _db.SaveChangesAsync(ct);
                        enriched++;
                        if (rounded.HasValue && rounded.Value != 0)
                            displayable++;
                        else
                            ignored++;
                    }
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    var isAudit = auditSet.Contains(Normalize(systemName));
                    if (isAudit)
                        _log.LogWarning(ex, "[EdsmDelta] {System} — erreur: {Message}", systemName, ex.Message);
                    else
                        _log.LogDebug(ex, "[EdsmDelta] {System} — erreur (ignorée)", systemName);
                }
            }
        }

        var error = lastEx != null ? $"erreur / {lastEx.Message}" : null;
        _log.LogInformation("[EdsmDelta] Terminé: {BatchCount} batch(s), enrichis={Enriched} affichables={Displayable} ignorés={Ignored}",
            batches.Count, enriched, displayable, ignored);
        return new EdsmEnrichmentResult(mode, enriched, displayable, ignored, error);
    }

    /// <summary>
    /// Récupère le delta d'influence (72h) pour un système et une faction depuis EDSM factions API (showHistory=1).
    /// Fenêtre 72h pour correspondre à l'affichage EDSM. Retourne (delta en %, raison si absent).
    /// </summary>
    private async Task<(decimal? Delta, string? Reason)> FetchDeltaAsync(HttpClient client, string systemName, string factionName, CancellationToken ct)
    {
        var url = $"https://www.edsm.net/api-system-v1/factions?systemName={Uri.EscapeDataString(systemName)}&showHistory=1";
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url, ct);
        }
        catch (Exception ex)
        {
            return (null, $"EDSM indisponible: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
            return (null, $"HTTP {(int)response.StatusCode}");

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("factions", out var factionsEl))
            return (null, "pas de factions");

        var factionNameNorm = factionName.Trim();
        JsonElement? match = null;
        foreach (var f in factionsEl.EnumerateArray())
        {
            var name = f.GetProperty("name").GetString();
            if (string.Equals(name?.Trim(), factionNameNorm, StringComparison.OrdinalIgnoreCase))
            {
                match = f;
                break;
            }
        }

        if (match == null)
            return (null, "faction non trouvée");

        var faction = match.Value;
        if (!faction.TryGetProperty("influence", out var infEl))
            return (null, "influence absente");

        var currentInfluence = infEl.GetDecimal();
        decimal? delta = null;

        if (faction.TryGetProperty("influenceHistory", out var histEl) && histEl.ValueKind == JsonValueKind.Object)
        {
            var nowTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // 72h (3 jours) pour aligner avec la tendance affichée par EDSM sur leur page système.
            // Une fenêtre 24h tombait souvent sur le même tick BGS quotidien → delta 0.
            const long windowSeconds = 72 * 3600;
            var targetTs = nowTs - windowSeconds;
            long? bestTs = null;
            decimal bestVal = 0;

            foreach (var prop in histEl.EnumerateObject())
            {
                if (long.TryParse(prop.Name, out var ts) && ts <= targetTs)
                {
                    if (bestTs == null || ts > bestTs)
                    {
                        bestTs = ts;
                        bestVal = prop.Value.GetDecimal();
                    }
                }
            }

            if (bestTs.HasValue)
                delta = (currentInfluence - bestVal) * 100;
            else
                return (null, "aucune donnée historique 72h");
        }
        else
            return (null, "influenceHistory absent");

        return (delta, null);
    }

    private static string Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
}

/// <summary>Résultat de l'enrichissement EDSM (tendances 24h).</summary>
public record EdsmEnrichmentResult(string Mode, int EnrichedCount, int DisplayableCount, int IgnoredCount, string? Error);
