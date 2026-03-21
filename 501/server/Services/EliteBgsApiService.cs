using System.Text.Json;

namespace GuildDashboard.Server.Services;

/// <summary>Client pour l'API Elite BGS (elitebgs.app) — source des données BGS réelles.</summary>
/// <remarks>
/// Endpoints utilisés :
/// - GET /api/ebgs/v5/factions?name={factionName} — présence de la faction par système avec influence, state.
/// Structure attendue : { docs: [{ name, faction_presence: [{ system_name, influence, state, ... }] }] }
/// </remarks>
public class EliteBgsApiService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<EliteBgsApiService> _log;
    private const string DefaultBaseUrl = "https://elitebgs.app/api/ebgs/v5";

    public EliteBgsApiService(HttpClient http, IConfiguration config, ILogger<EliteBgsApiService> log)
    {
        _http = http;
        _config = config;
        _log = log;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add("User-Agent", "GuildDashboard/1.0");
    }

    private string BaseUrl => _config["Bgs:EliteBgsBaseUrl"] ?? DefaultBaseUrl;

    /// <summary>Récupère les données BGS d'une faction par son nom exact.</summary>
    /// <returns>Présences par système (influence, state) ou null si indisponible.</returns>
    public async Task<IReadOnlyList<EliteBgsFactionPresence>?> GetFactionPresenceAsync(string factionName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(factionName))
            return null;

        var url = $"{BaseUrl}/factions?name={Uri.EscapeDataString(factionName)}";
        _log.LogInformation("[EliteBgs] Appel URL={Url} faction={FactionName}", url, factionName);

        try
        {
            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            _log.LogInformation("[EliteBgs] Réponse brute longueur={Length} preview={Preview}",
                json.Length, json.Length > 500 ? json[..500] + "..." : json);

            var parsed = ParseFactionPresence(json, factionName);
            if (parsed != null)
            {
                foreach (var p in parsed)
                    _log.LogInformation("[EliteBgs] Système EliteBGS: name={Name} influence={Influence} state={State}",
                        p.SystemName, p.InfluencePercent, p.State ?? "(null)");
                _log.LogInformation("[EliteBgs] Total systèmes retournés par Elite BGS: {Count}", parsed.Count);
            }
            else
            {
                try
                {
                    using var d = JsonDocument.Parse(json);
                    var keys = string.Join(", ", d.RootElement.EnumerateObject().Select(p => p.Name));
                    _log.LogWarning("[EliteBgs] Parse échoué ou docs vides. Clés racine: [{Keys}]. Structure attendue: docs[].faction_presence[].system_name", keys);
                }
                catch { _log.LogWarning("[EliteBgs] Parse échoué. JSON invalide ou structure inattendue."); }
            }

            return parsed;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[EliteBgs] Erreur URL={Url}: {Message}", url, ex.Message);
            return null;
        }
    }

    /// <summary>Récupère toutes les factions d'un système (pour calculer IsControlled, IsThreatened).</summary>
    /// <returns>Liste des influences par faction, ou null si indisponible.</returns>
    public async Task<IReadOnlyList<(string FactionName, decimal InfluencePercent)>?> GetSystemFactionsAsync(string systemName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(systemName))
            return null;

        var url = $"{BaseUrl}/systems?name={Uri.EscapeDataString(systemName)}";
        try
        {
            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseSystemFactions(json, systemName);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[EliteBgs] Erreur systems name={SystemName}: {Message}", systemName, ex.Message);
            return null;
        }
    }

    /// <summary>Parse la réponse systems Elite BGS.</summary>
    private static IReadOnlyList<(string, decimal)>? ParseSystemFactions(string json, string systemName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("docs", out var docs) || docs.GetArrayLength() == 0)
                return null;

            foreach (var sys in docs.EnumerateArray())
            {
                var name = sys.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name) || !string.Equals(name, systemName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!sys.TryGetProperty("factions", out var factions))
                    return null;

                var list = new List<(string, decimal)>();
                foreach (var f in factions.EnumerateArray())
                {
                    var factionName = f.TryGetProperty("name", out var fn) ? fn.GetString() ?? "" : "";
                    var influence = 0m;
                    if (f.TryGetProperty("influence", out var inf) && inf.TryGetDecimal(out var val))
                        influence = val <= 1 ? val * 100 : val;
                    list.Add((factionName, influence));
                }
                return list;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parse la réponse JSON Elite BGS (structure flexible).</summary>
    private static IReadOnlyList<EliteBgsFactionPresence>? ParseFactionPresence(string json, string factionName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("docs", out var docs) || docs.GetArrayLength() == 0)
                return null;

            var list = new List<EliteBgsFactionPresence>();
            foreach (var faction in docs.EnumerateArray())
            {
                var name = faction.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name) || !string.Equals(name, factionName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!faction.TryGetProperty("faction_presence", out var presence) && !faction.TryGetProperty("presence", out presence))
                    continue;

                foreach (var p in presence.EnumerateArray())
                {
                    var systemName = (p.TryGetProperty("system_name", out var sn) ? sn.GetString() : null)
                        ?? (p.TryGetProperty("systemName", out var sn2) ? sn2.GetString() : null)
                        ?? (p.TryGetProperty("name", out var sn3) ? sn3.GetString() : null);
                    if (string.IsNullOrEmpty(systemName))
                        continue;

                    var influence = 0m;
                    if (p.TryGetProperty("influence", out var inf))
                    {
                        if (inf.ValueKind == JsonValueKind.Number && inf.TryGetDecimal(out var val))
                            influence = val * 100m; // API renvoie souvent 0.0-1.0
                        else if (inf.ValueKind == JsonValueKind.Number)
                            influence = inf.GetDecimal();
                    }

                    // Certaines APIs renvoient déjà un pourcentage
                    if (influence > 0 && influence <= 1)
                        influence *= 100;

                    var state = p.TryGetProperty("state", out var st) ? st.GetString() : null;

                    list.Add(new EliteBgsFactionPresence(systemName, influence, state));
                }
                break; // Une seule faction correspondante
            }

            return list.Count > 0 ? list : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Présence d'une faction dans un système (données Elite BGS).</summary>
public record EliteBgsFactionPresence(string SystemName, decimal InfluencePercent, string? State);
