// Feature archived – no reliable external data source for Faction → Systems → Influence %.
// EDSM ne fournit pas l'influence %. Conserver pour R&D futur. Voir docs/GUILD-SYSTEMS.md.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuildDashboard.Server.Services;

/// <summary>Client pour l'API EDSM Systems v1. Enrichissement BGS (faction contrôlante, état).</summary>
/// <remarks>Voir docs/INTEGRATION-EDSM.md. Pas d'influence %, pas de stations. Batch /api-v1/systems.</remarks>
public class EdsmApiService
{
    private readonly HttpClient _http;
    private readonly ILogger<EdsmApiService> _log;

    private const string BaseUrl = "https://www.edsm.net/api-v1/systems";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public EdsmApiService(HttpClient http, ILogger<EdsmApiService> log)
    {
        _http = http;
        _log = log;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add("User-Agent", "GuildDashboard/1.0");
    }

    /// <summary>Récupère les infos systèmes en batch (faction contrôlante, factionState).</summary>
    /// <param name="systemNames">Noms des systèmes (ex: Hip 4332, Mayang).</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Dictionnaire Name (EDSM-normalized) → (Faction, FactionState). Systèmes non trouvés absents.</returns>
    public async Task<IReadOnlyDictionary<string, EdsmSystemInfo>> GetSystemsBatchAsync(
        IReadOnlyList<string> systemNames,
        CancellationToken ct = default)
    {
        if (systemNames.Count == 0)
            return new Dictionary<string, EdsmSystemInfo>();

        var query = string.Join("&", systemNames.Select(n => "systemName[]=" + Uri.EscapeDataString(n)));
        var url = $"{BaseUrl}?{query}&showInformation=1";

        try
        {
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("[Edsm] HTTP {Code} for {Url}", response.StatusCode, url);
                return new Dictionary<string, EdsmSystemInfo>();
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var list = JsonSerializer.Deserialize<List<EdsmSystemResponse>>(json, JsonOptions);
            if (list == null || list.Count == 0)
                return new Dictionary<string, EdsmSystemInfo>();

            var result = new Dictionary<string, EdsmSystemInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in list)
            {
                if (string.IsNullOrEmpty(item.Name))
                    continue;
                var info = item.Information;
                result[item.Name] = new EdsmSystemInfo(
                    info?.Faction,
                    info?.FactionState
                );
            }
            _log.LogInformation("[Edsm] Batch: {Requested} requested, {Returned} returned", systemNames.Count, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Edsm] Erreur lors de l'appel batch");
            throw;
        }
    }

    private class EdsmSystemResponse
    {
        public string? Name { get; set; }
        public EdsmInformation? Information { get; set; }
    }

    private class EdsmInformation
    {
        public string? Faction { get; set; }
        public string? FactionState { get; set; }
    }

    public record EdsmSystemInfo(string? Faction, string? FactionState);

    /// <summary>Récupère les coordonnées galactiques des systèmes en batch (EDSM showCoordinates=1).</summary>
    /// <returns>Dictionnaire Name → (X, Y, Z). Systèmes sans coords ou non trouvés absents.</returns>
    public async Task<IReadOnlyDictionary<string, (double X, double Y, double Z)>> GetSystemsCoordsBatchAsync(
        IReadOnlyList<string> systemNames,
        CancellationToken ct = default)
    {
        if (systemNames.Count == 0)
            return new Dictionary<string, (double, double, double)>();

        var query = string.Join("&", systemNames.Select(n => "systemName[]=" + Uri.EscapeDataString(n)));
        var url = $"{BaseUrl}?{query}&showCoordinates=1";

        try
        {
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("[Edsm] Coords HTTP {Code} for {Url}", response.StatusCode, url);
                return new Dictionary<string, (double, double, double)>();
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var list = JsonSerializer.Deserialize<List<EdsmCoordsResponse>>(json, JsonOptions);

            if (list == null || list.Count == 0)
                return new Dictionary<string, (double, double, double)>();

            var result = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in list)
            {
                if (string.IsNullOrEmpty(item.Name) || item.Coords == null)
                    continue;
                var c = item.Coords;
                result[item.Name] = (c.X, c.Y, c.Z);
            }
            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Edsm] Erreur lors de l'appel coords batch");
            throw;
        }
    }

    private class EdsmCoordsResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("coords")]
        public EdsmCoords? Coords { get; set; }
    }

    private class EdsmCoords
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        public double Z { get; set; }
    }
}
