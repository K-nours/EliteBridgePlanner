using System.Net.Http.Json;
using System.Text.Json;

namespace GuildDashboard.Server.Services;

/// <summary>Service pour appeler l'API Inara (getCommanderProfile, etc.).</summary>
public class InaraApiService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private const string InaraApiUrl = "https://inara.cz/inapi/v1/";

    public InaraApiService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.Add("User-Agent", "EliteBridgePlanner/1.0");
    }

    /// <summary>Récupère le profil d'un commandant (squadron, avatar, etc.).</summary>
    public async Task<InaraCommanderProfile?> GetCommanderProfileAsync(string searchName, CancellationToken ct = default)
    {
        var apiKey = _config["Inara:ApiKey"];
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
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("events", out var events) && events.GetArrayLength() > 0)
            {
                var evt = events[0];
                if (evt.TryGetProperty("eventStatus", out var status) && status.GetInt32() == 200
                    && evt.TryGetProperty("eventData", out var data))
                {
                    return ParseProfile(data);
                }
            }
        }
        catch (Exception)
        {
            // Log in production
        }

        return null;
    }

    private static InaraCommanderProfile? ParseProfile(JsonElement data)
    {
        var userName = data.TryGetProperty("userName", out var u) ? u.GetString() : null;
        var commanderName = data.TryGetProperty("commanderName", out var c) ? c.GetString() : userName;
        var avatarUrl = data.TryGetProperty("avatarImageURL", out var a) ? a.GetString() : null;

        InaraSquadron? squadron = null;
        if (data.TryGetProperty("commanderSquadron", out var sq))
        {
            squadron = new InaraSquadron(
                sq.TryGetProperty("SquadronID", out var id) ? id.GetInt32() : 0,
                sq.TryGetProperty("SquadronName", out var sn) ? sn.GetString() ?? "" : ""
            );
        }

        return new InaraCommanderProfile(commanderName ?? userName ?? "", userName ?? "", avatarUrl, squadron);
    }
}

public record InaraCommanderProfile(string CommanderName, string UserName, string? AvatarImageUrl, InaraSquadron? Squadron);
public record InaraSquadron(int SquadronId, string SquadronName);
