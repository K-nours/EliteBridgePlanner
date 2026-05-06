// Feature archived – getCommanderProfile blocked by Inara (app not authorized).
// Conserver pour R&D futur. Voir docs/INTEGRATION-INARA.md.

using System.Net.Http.Json;
using System.Text.Json;

namespace GuildDashboard.Server.Services;

/// <summary>Service pour appeler l'API Inara (getCommanderProfile, etc.).</summary>
public class InaraApiService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly InaraApiUserSettingsStore _inaraApiUser;
    private readonly ILogger<InaraApiService> _log;
    private const string InaraApiUrl = "https://inara.cz/inapi/v1/";

    public InaraApiService(HttpClient http, IConfiguration config, InaraApiUserSettingsStore inaraApiUser, ILogger<InaraApiService> log)
    {
        _http = http;
        _config = config;
        _inaraApiUser = inaraApiUser;
        _log = log;
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.Add("User-Agent", "EliteBridgePlanner/1.0");
    }

    /// <summary>Résultat brut de getCommanderProfile (pour validation du match et logs).</summary>
    public record GetCommanderProfileResult(
        int EventStatus,
        string? EventStatusText,
        string? CommanderName,
        string? AvatarImageUrl,
        IReadOnlyList<string> OtherNamesFound,
        bool HasEventData
    );

    /// <summary>Récupère le profil d'un commandant (squadron, avatar, etc.). Retourne toujours un résultat pour validation.</summary>
    public async Task<GetCommanderProfileResult?> GetCommanderProfileAsync(string searchName, CancellationToken ct = default)
    {
        var apiKey = _inaraApiUser.ResolveApiKey(_config);
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var payload = new
        {
            header = new
            {
                appName = "EliteBridgePlanner",
                appVersion = "1.0",
                isBeingDeveloped = true,
                APIkey = apiKey
            },
            events = new[]
            {
                new
                {
                    eventName = "getCommanderProfile",
                    eventTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    eventData = new { searchName }
                }
            }
        };

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = null };
        try
        {
            var response = await _http.PostAsJsonAsync(InaraApiUrl, payload, jsonOpts, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("header", out var header) &&
                header.TryGetProperty("eventStatus", out var hs) && hs.GetInt32() != 200)
            {
                var hText = header.TryGetProperty("eventStatusText", out var ht) ? ht.GetString() : null;
                _log.LogWarning("Inara getCommanderProfile '{SearchName}': erreur header eventStatus={Status} eventStatusText={Text}",
                    searchName, hs.GetInt32(), hText ?? "");
                return new GetCommanderProfileResult(hs.GetInt32(), hText ?? "", null, null, Array.Empty<string>(), HasEventData: false);
            }

            response.EnsureSuccessStatusCode();
            if (root.TryGetProperty("events", out var events) && events.GetArrayLength() > 0)
            {
                var evt = events[0];
                var status = evt.TryGetProperty("eventStatus", out var s) ? s.GetInt32() : 0;
                var statusText = evt.TryGetProperty("eventStatusText", out var st) ? st.GetString() : null;

                if (evt.TryGetProperty("eventData", out var data))
                {
                    var (commanderName, avatarUrl, otherNamesFound) = ParseProfileData(data);
                    LogFullInaraResponse(searchName, status, statusText, commanderName, avatarUrl, otherNamesFound);
                    return new GetCommanderProfileResult(status, statusText, commanderName, avatarUrl, otherNamesFound, HasEventData: true);
                }

                _log.LogInformation(
                    "Inara getCommanderProfile: searchName={SearchName} eventStatus={EventStatus} eventStatusText={EventStatusText} hasEventData=false",
                    searchName, status, statusText ?? "");
                return new GetCommanderProfileResult(status, statusText, null, null, Array.Empty<string>(), HasEventData: false);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Inara getCommanderProfile '{SearchName}': request failed", searchName);
        }

        return null;
    }

    private static (string? CommanderName, string? AvatarImageUrl, IReadOnlyList<string> OtherNamesFound) ParseProfileData(JsonElement data)
    {
        var userName = data.TryGetProperty("userName", out var u) ? u.GetString() : null;
        var commanderName = data.TryGetProperty("commanderName", out var c) ? c.GetString() : userName;
        var avatarUrl = data.TryGetProperty("avatarImageURL", out var a) ? a.GetString() : null;
        var otherNames = Array.Empty<string>();
        if (data.TryGetProperty("otherNamesFound", out var onf) && onf.ValueKind == JsonValueKind.Array)
        {
            otherNames = onf.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrEmpty(s))
                .Cast<string>()
                .ToArray();
        }
        return (commanderName ?? userName, avatarUrl, otherNames);
    }

    private void LogFullInaraResponse(string searchName, int eventStatus, string? eventStatusText,
        string? commanderName, string? avatarImageUrl, IReadOnlyList<string> otherNamesFound)
    {
        _log.LogInformation(
            "Inara getCommanderProfile réponse: searchName={SearchName} eventStatus={EventStatus} eventStatusText={EventStatusText} commanderName={CommanderName} avatarImageURL={AvatarUrl} otherNamesFound=[{OtherNames}]",
            searchName,
            eventStatus,
            eventStatusText ?? "",
            commanderName ?? "",
            avatarImageUrl ?? "",
            string.Join(", ", otherNamesFound));
    }
}

public record InaraCommanderProfile(string CommanderName, string UserName, string? AvatarImageUrl, InaraSquadron? Squadron);
public record InaraSquadron(int SquadronId, string SquadronName);
