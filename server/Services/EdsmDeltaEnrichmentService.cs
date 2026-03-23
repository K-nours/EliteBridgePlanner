using System.Text.Json;
using GuildDashboard.Server.Data;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Enrichissement post-import Inara : récupère la tendance d'influence 24h depuis EDSM (api-system-v1/factions?showHistory=1)
/// et la stocke dans ControlledSystem.InfluenceDelta24h. Ne bloque jamais l'import Inara en cas d'erreur EDSM.
/// </summary>
public class EdsmDeltaEnrichmentService
{
    private readonly GuildDashboardDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<EdsmDeltaEnrichmentService> _log;

    private static readonly string[] AuditSystemNames = ["HIP 4332", "Mayang", "Nanapan"];
    private const int DelayBetweenCallsMs = 300;

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
    /// Après un import Inara réussi, enrichit les ControlledSystems avec InfluenceDelta24h depuis EDSM.
    /// Si EDSM échoue pour un système, l'import Inara reste valide (on continue pour les autres).
    /// </summary>
    public async Task EnrichAfterImportAsync(int guildId, IReadOnlyList<string> systemNames, CancellationToken ct = default)
    {
        if (systemNames.Count == 0) return;

        var guild = await _db.Guilds
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == guildId, ct);
        if (guild == null) return;

        var factionName = guild.FactionName ?? guild.Name;
        if (string.IsNullOrWhiteSpace(factionName))
        {
            _log.LogInformation("[EdsmDelta] FactionName non configuré, enrichissement ignoré");
            return;
        }

        var auditSet = new HashSet<string>(AuditSystemNames.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Add("User-Agent", "GuildDashboard/1.0");

        foreach (var systemName in systemNames.Distinct())
        {
            try
            {
                await Task.Delay(DelayBetweenCallsMs, ct);

                var (deltaRaw, reason) = await FetchDeltaAsync(client, systemName, factionName, ct);
                if (reason != null)
                {
                    if (auditSet.Contains(Normalize(systemName)))
                        _log.LogInformation("[EdsmDelta] {System} — absent: {Reason}", systemName, reason);
                    continue;
                }

                var stored = (decimal?)null;
                var cs = await _db.ControlledSystems
                    .FirstOrDefaultAsync(c => c.GuildId == guildId && c.Name == systemName, ct);
                if (cs != null && !cs.IsFromSeed)
                {
                    cs.InfluenceDelta24h = deltaRaw;
                    cs.UpdatedAt = DateTime.UtcNow;
                    stored = deltaRaw;
                    await _db.SaveChangesAsync(ct);
                }

                if (auditSet.Contains(Normalize(systemName)))
                    _log.LogInformation("[EdsmDelta] {System} — delta brut EDSM={Raw:F4} delta stocké={Stored}",
                        systemName, deltaRaw ?? 0, stored?.ToString("F4") ?? "null");
            }
            catch (Exception ex)
            {
                var isAudit = auditSet.Contains(Normalize(systemName));
                if (isAudit)
                    _log.LogWarning(ex, "[EdsmDelta] {System} — erreur EDSM: {Message}", systemName, ex.Message);
                else
                    _log.LogDebug(ex, "[EdsmDelta] {System} — erreur (ignorée)", systemName);
            }
        }
    }

    /// <summary>
    /// Récupère le delta 24h pour un système et une faction depuis EDSM factions API (showHistory=1).
    /// Retourne (delta en %, raison si absent).
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
            var targetTs = nowTs - 86400; // 24h ago
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
                return (null, "aucune donnée historique 24h");
        }
        else
            return (null, "influenceHistory absent");

        return (delta, null);
    }

    private static string Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
}
