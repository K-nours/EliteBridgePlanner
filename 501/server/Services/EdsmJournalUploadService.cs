using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Envoie des lignes du journal Elite vers EDSM (API Journal v1).
/// Voir https://www.edsm.net/en_GB/api-journal-v1
/// </summary>
public sealed class EdsmJournalUploadService
{
    public const string ApiUrl = "https://www.edsm.net/api-journal-v1";
    private const string RawFileName = "frontier-journal-raw.json";
    private const string SoftwareName = "EliteBridgePlanner";

    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IWebHostEnvironment _env;
    private readonly EdsmJournalSettingsStore _edsmUserSettings;
    private readonly ILogger<EdsmJournalUploadService> _log;

    public EdsmJournalUploadService(
        IConfiguration config,
        IHttpClientFactory httpFactory,
        IWebHostEnvironment env,
        EdsmJournalSettingsStore edsmUserSettings,
        ILogger<EdsmJournalUploadService> log)
    {
        _config = config;
        _httpFactory = httpFactory;
        _env = env;
        _edsmUserSettings = edsmUserSettings;
        _log = log;
    }

    private string DataDir => Path.Combine(_env.ContentRootPath ?? AppContext.BaseDirectory ?? ".", "Data", "frontier-journal");

    /// <summary>Fichier <c>Data/edsm-journal-user.json</c> en priorité, sinon configuration.</summary>
    private (string? commander, string? apiKey) ResolveEdsmCredentials()
    {
        var file = _edsmUserSettings.Load();
        var commander = !string.IsNullOrWhiteSpace(file?.CommanderName)
            ? file!.CommanderName.Trim()
            : _config["Edsm:CommanderName"]?.Trim();
        var apiKey = !string.IsNullOrWhiteSpace(file?.ApiKey)
            ? file!.ApiKey.Trim()
            : _config["Edsm:ApiKey"]?.Trim();
        return (commander, apiKey);
    }

    /// <summary>
    /// Test : envoie une seule ligne (FSDJump ou Location) issue du raw Frontier vers EDSM.
    /// </summary>
    public async Task<EdsmJournalTestUploadResult> TestUploadOneFromFrontierRawAsync(
        string? date,
        string? systemName,
        CancellationToken ct = default)
    {
        var (commander, apiKey) = ResolveEdsmCredentials();
        if (string.IsNullOrEmpty(commander) || string.IsNullOrEmpty(apiKey))
        {
            return EdsmJournalTestUploadResult.Fail(
                "Indiquez le nom du commandant EDSM et la clé API : Paramètres de l'app (section EDSM), ou Edsm:CommanderName / Edsm:ApiKey dans appsettings / user-secrets.");
        }

        var rawPath = Path.Combine(DataDir, RawFileName);
        if (!File.Exists(rawPath))
        {
            return EdsmJournalTestUploadResult.Fail(
                "Aucun journal Frontier local (Data/frontier-journal/frontier-journal-raw.json). Synchronisez d'abord avec Frontier.");
        }

        Dictionary<string, FrontierJournalRawEntry> raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, FrontierJournalRawEntry>>(await File.ReadAllTextAsync(rawPath, ct))
                  ?? new Dictionary<string, FrontierJournalRawEntry>();
        }
        catch (Exception ex)
        {
            return EdsmJournalTestUploadResult.Fail("Lecture raw impossible : " + ex.Message);
        }

        string dateKey;
        FrontierJournalRawEntry entry;

        if (!string.IsNullOrWhiteSpace(date))
        {
            dateKey = date.Trim();
            if (!raw.TryGetValue(dateKey, out entry!) || entry == null)
                return EdsmJournalTestUploadResult.Fail($"Date {dateKey} introuvable dans le raw.");
        }
        else
        {
            var cand = raw
                .Where(kv => string.Equals(kv.Value.Status, "success", StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(kv.Value.Payload)
                             && kv.Value.Payload.Trim() != "[]")
                .OrderByDescending(kv => kv.Key)
                .FirstOrDefault();
            if (cand.Value == null)
            {
                return EdsmJournalTestUploadResult.Fail(
                    "Pas de jour « success » avec entrées. Passez date=yyyy-MM-dd dans le corps JSON.");
            }

            dateKey = cand.Key;
            entry = cand.Value;
        }

        if (!string.Equals(entry.Status, "success", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(entry.Payload))
        {
            return EdsmJournalTestUploadResult.Fail($"Jour {dateKey} : pas de payload (status={entry.Status}).");
        }

        using var doc = JsonDocument.Parse(entry.Payload);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return EdsmJournalTestUploadResult.Fail("Le journal du jour n'est pas un tableau JSON.");

        var arr = doc.RootElement;
        var pick = SelectJournalLine(arr, systemName, out var failHint);
        if (pick == null)
            return EdsmJournalTestUploadResult.Fail(failHint ?? "Aucun FSDJump/Location exploitable.");

        var (line, index, gameVer, gameBuild) = pick.Value;
        if (string.IsNullOrEmpty(gameVer) || string.IsNullOrEmpty(gameBuild))
        {
            return EdsmJournalTestUploadResult.Fail(
                "Impossible de déterminer fromGameVersion / fromGameBuild (événement LoadGame absent avant la ligne choisie).");
        }

        if (JsonNode.Parse(line.GetRawText()) is not JsonObject lineObj)
            return EdsmJournalTestUploadResult.Fail("La ligne journal n'est pas un objet JSON.");
        TryEnrichTransient(arr, index, lineObj);

        var swVer = typeof(EdsmJournalUploadService).Assembly.GetName().Version?.ToString(3) ?? "501";
        var body = new JsonObject
        {
            ["commanderName"] = commander,
            ["apiKey"] = apiKey,
            ["fromSoftware"] = SoftwareName,
            ["fromSoftwareVersion"] = swVer,
            ["fromGameVersion"] = gameVer,
            ["fromGameBuild"] = gameBuild,
            ["message"] = lineObj,
        };

        var json = body.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(45);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        HttpResponseMessage resp;
        try
        {
            resp = await client.PostAsync(ApiUrl, content, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[EDSM Journal] POST échoué");
            return EdsmJournalTestUploadResult.Fail("Requête EDSM : " + ex.Message, dateKey, failHint);
        }

        var respText = await resp.Content.ReadAsStringAsync(ct);
        int? msgnum = null;
        string? msg = null;
        try
        {
            using var respDoc = JsonDocument.Parse(respText);
            var root = respDoc.RootElement;
            if (root.TryGetProperty("msgnum", out var mn) && mn.TryGetInt32(out var n))
                msgnum = n;
            if (root.TryGetProperty("msg", out var m))
                msg = m.GetString();
        }
        catch
        {
            msg = respText.Length > 500 ? respText[..500] + "…" : respText;
        }

        var lineEvent = line.TryGetProperty("event", out var ev) ? ev.GetString() : null;
        var starSys = line.TryGetProperty("StarSystem", out var ss) ? ss.GetString() : null;

        // 100 OK, 101 déjà stocké, 102 plus vieux que stocké — utiles pour un test manuel
        var edsmOk = msgnum is 100 or 101 or 102;
        var success = resp.IsSuccessStatusCode && edsmOk;

        return new EdsmJournalTestUploadResult
        {
            Success = success,
            Error = success ? null : (msg ?? $"HTTP {(int)resp.StatusCode}"),
            HttpStatus = (int)resp.StatusCode,
            MsgNum = msgnum,
            Msg = msg,
            DateUsed = dateKey,
            Detail = failHint,
            Event = lineEvent,
            StarSystem = starSys,
        };
    }

    private static (JsonElement line, int index, string gameVer, string gameBuild)? SelectJournalLine(
        JsonElement arr,
        string? systemFilter,
        out string? failHint)
    {
        failHint = null;
        var n = arr.GetArrayLength();
        var normalizedFilter = systemFilter?.Trim();
        int? fsdIdx = null;
        int? locIdx = null;

        for (var i = 0; i < n; i++)
        {
            var el = arr[i];
            if (!el.TryGetProperty("event", out var evEl))
                continue;
            var ev = evEl.GetString() ?? "";
            if (!el.TryGetProperty("StarSystem", out var starEl))
                continue;
            var star = starEl.GetString() ?? "";
            if (IsCqcSystem(star))
                continue;

            var matchFilter = string.IsNullOrEmpty(normalizedFilter) ||
                              string.Equals(star, normalizedFilter, StringComparison.OrdinalIgnoreCase);
            if (!matchFilter)
                continue;

            if (string.Equals(ev, "FSDJump", StringComparison.OrdinalIgnoreCase))
            {
                fsdIdx = i;
                break;
            }

            if (string.Equals(ev, "Location", StringComparison.OrdinalIgnoreCase) && locIdx == null)
                locIdx = i;
        }

        int idx;
        if (fsdIdx.HasValue)
            idx = fsdIdx.Value;
        else if (locIdx.HasValue)
            idx = locIdx.Value;
        else
        {
            failHint = string.IsNullOrEmpty(normalizedFilter)
                ? "Aucun FSDJump/Location utilisable ce jour."
                : $"Aucun FSDJump/Location pour « {normalizedFilter} » ce jour-là.";
            return null;
        }

        var line = arr[idx];
        var gameVer = "";
        var gameBuild = "";
        for (var i = 0; i <= idx; i++)
        {
            var el = arr[i];
            if (!el.TryGetProperty("event", out var evEl))
                continue;
            if (!string.Equals(evEl.GetString(), "LoadGame", StringComparison.OrdinalIgnoreCase))
                continue;
            if (TryGetGameMeta(el, out var gv, out var gb))
            {
                gameVer = gv;
                gameBuild = gb;
            }
        }

        var evName = line.TryGetProperty("event", out var evP) ? evP.GetString() : "?";
        var sysPick = line.TryGetProperty("StarSystem", out var ssP) ? ssP.GetString() : "?";
        failHint = $"Sélection : index {idx}, {evName} → {sysPick}";
        return (line, idx, gameVer, gameBuild);
    }

    private static bool IsCqcSystem(string star) =>
        string.Equals(star, "Proving Ground", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(star, "CQC", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetGameMeta(JsonElement loadGame, out string ver, out string build)
    {
        ver = "";
        build = "";
        if (loadGame.TryGetProperty("gameversion", out var gv))
            ver = gv.GetString() ?? "";
        else if (loadGame.TryGetProperty("GameVersion", out gv))
            ver = gv.GetString() ?? "";
        if (loadGame.TryGetProperty("build", out var gb))
            build = gb.GetString() ?? "";
        else if (loadGame.TryGetProperty("Build", out gb))
            build = gb.GetString() ?? "";
        ver = ver.Trim();
        build = build.Trim();
        return ver.Length > 0 && build.Length > 0;
    }

    private static string? ReadStarSystem(JsonElement line, out string? star)
    {
        star = line.TryGetProperty("StarSystem", out var s) ? s.GetString() : null;
        return star;
    }

    /// <summary>Répliqué simplifié de l’état transitoire EDSM (parcours jusqu’à l’index choisi).</summary>
    private static void TryEnrichTransient(JsonElement arr, int index, JsonObject lineNode)
    {
        ulong? systemId = null;
        string? systemName = null;
        JsonNode? coords = null;

        for (var i = 0; i <= index; i++)
        {
            var el = arr[i];
            if (!el.TryGetProperty("event", out var evEl))
                continue;
            var ev = evEl.GetString() ?? "";
            if (string.Equals(ev, "LoadGame", StringComparison.OrdinalIgnoreCase))
            {
                systemId = null;
                systemName = null;
                coords = null;
                continue;
            }

            if (!string.Equals(ev, "Location", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ev, "FSDJump", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!el.TryGetProperty("StarSystem", out var starEl))
                continue;
            var name = starEl.GetString() ?? "";
            if (string.IsNullOrEmpty(name) || IsCqcSystem(name))
                continue;

            systemName = name;
            if (el.TryGetProperty("SystemAddress", out var sa) && sa.ValueKind == JsonValueKind.Number)
                systemId = sa.GetUInt64();
            else
                systemId = null;
            if (el.TryGetProperty("StarPos", out var sp) && sp.ValueKind == JsonValueKind.Array)
                coords = JsonNode.Parse(sp.GetRawText());
            else
                coords = null;
        }

        if (systemId.HasValue)
            lineNode["_systemAddress"] = systemId.Value;
        if (systemName != null)
            lineNode["_systemName"] = systemName;
        if (coords != null)
            lineNode["_systemCoordinates"] = coords;
    }
}

public sealed class EdsmJournalTestUploadResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int? HttpStatus { get; init; }
    public int? MsgNum { get; init; }
    public string? Msg { get; init; }
    public string? DateUsed { get; init; }
    public string? Detail { get; init; }
    public string? Event { get; init; }
    public string? StarSystem { get; init; }

    public static EdsmJournalTestUploadResult Fail(string message, string? dateUsed = null, string? detail = null) =>
        new()
        {
            Success = false,
            Error = message,
            DateUsed = dateUsed,
            Detail = detail,
        };
}
