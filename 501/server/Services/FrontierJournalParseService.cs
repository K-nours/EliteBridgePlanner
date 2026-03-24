using System.Globalization;
using System.Text.Json;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Parsing incrémental du journal CAPI (raw.json) vers agrégats par système.
/// Règles : visité = FSDJump / CarrierJump / Location ; full scan = FSSAllBodiesFound ;
/// « découvert par moi » = au moins un Scan avec WasDiscovered=false (premier corps).
/// Le brut journal est du NDJSON (une ligne JSON par événement) ou legacy tableau JSON.
/// </summary>
public class FrontierJournalParseService
{
    public const int CurrentParseVersion = 3;

    private const string ParseStateFileName = "frontier-journal-parse-state.json";
    private const string DerivedFileName = "frontier-journal-derived.json";
    private const string RawFileName = "frontier-journal-raw.json";

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FrontierJournalParseService> _log;
    private readonly object _runLock = new();
    private Task? _parseTask;

    public FrontierJournalParseService(IWebHostEnvironment env, ILogger<FrontierJournalParseService> log)
    {
        _env = env;
        _log = log;
    }

    private string DataDir => Path.Combine(_env.ContentRootPath ?? AppContext.BaseDirectory ?? ".", "Data", "frontier-journal");

    /// <summary>Lance un lot de parsing (async). Retourne false si un lot est déjà en cours.</summary>
    public bool StartIncrementalParse(int batchSize = 40)
    {
        lock (_runLock)
        {
            if (_parseTask != null && !_parseTask.IsCompleted)
            {
                _log.LogWarning("[FrontierJournalParse] Lot ignoré : parsing déjà en cours");
                return false;
            }
            var size = Math.Clamp(batchSize, 1, 200);
            _parseTask = Task.Run(() => RunIncrementalParseAsync(size));
            return true;
        }
    }

    public FrontierJournalParseStatusDto GetParseStatus()
    {
        var state = LoadParseState();
        var derived = LoadDerived();
        var raw = LoadRawKeys();
        var pending = CountPendingDates(raw, state);
        var isRunning = _parseTask != null && !_parseTask.IsCompleted;
        return new FrontierJournalParseStatusDto
        {
            IsRunning = isRunning,
            ParseVersion = CurrentParseVersion,
            PendingDaysEstimate = pending,
            ParsedDaysCount = state?.Entries?.Count(e => e.Value.Status == "parsed_ok") ?? 0,
            ErrorDaysCount = state?.Entries?.Count(e => e.Value.Status == "parsed_error") ?? 0,
            SystemsCount = derived?.Systems?.Count ?? 0,
            DerivedUpdatedAt = derived?.UpdatedAt,
            LastParseError = state?.LastBatchError,
        };
    }

    public FrontierJournalDerivedResponseDto GetDerivedForMap()
    {
        var d = LoadDerived();
        var list = d?.Systems?.Values
            .Select(s => new FrontierJournalSystemDerivedDto
            {
                SystemName = s.SystemName,
                FirstVisitedAt = s.FirstVisitedAt,
                LastVisitedAt = s.LastVisitedAt,
                VisitCount = s.VisitCount,
                IsVisited = s.IsVisited,
                HasFirstDiscoveryBody = s.HasFirstDiscoveryBody,
                IsFullScanned = s.IsFullScanned,
                Provenance = s.LastProvenance,
                CoordsX = s.CoordsX,
                CoordsY = s.CoordsY,
                CoordsZ = s.CoordsZ,
            })
            .OrderBy(x => x.SystemName, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<FrontierJournalSystemDerivedDto>();
        return new FrontierJournalDerivedResponseDto
        {
            ParseVersion = d?.ParseVersion ?? CurrentParseVersion,
            UpdatedAt = d?.UpdatedAt,
            Systems = list,
        };
    }

    private async Task RunIncrementalParseAsync(int batchSize)
    {
        try
        {
            await Task.Yield();
            var raw = LoadRawDictionary();
            var state = LoadParseState() ?? new FrontierJournalParseState { Entries = new Dictionary<string, FrontierJournalParseDayState>() };
            state.Entries ??= new Dictionary<string, FrontierJournalParseDayState>();
            var derived = LoadDerived() ?? new FrontierJournalDerivedFile { Systems = new Dictionary<string, FrontierJournalDerivedSystem>(StringComparer.OrdinalIgnoreCase) };
            derived.Systems ??= new Dictionary<string, FrontierJournalDerivedSystem>(StringComparer.OrdinalIgnoreCase);

            if (derived.ParseVersion != 0 && derived.ParseVersion != CurrentParseVersion)
            {
                _log.LogWarning(
                    "[FrontierJournalParse] Version dérivée {Old} != parseur {Current} — réinitialisation agrégats et reparse des jours",
                    derived.ParseVersion,
                    CurrentParseVersion);
                derived.Systems = new Dictionary<string, FrontierJournalDerivedSystem>(StringComparer.OrdinalIgnoreCase);
                state.Entries.Clear();
            }

            var datesToProcess = raw
                .Where(kv => string.Equals(kv.Value.Status, "success", StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(kv.Value.Payload)
                             && kv.Value.Payload!.Trim() != "[]")
                .Select(kv => kv.Key)
                .OrderBy(d => d, StringComparer.Ordinal)
                .Where(d => NeedsParsing(d, state))
                .Take(batchSize)
                .ToList();

            state.LastBatchError = null;
            foreach (var dateStr in datesToProcess)
            {
                try
                {
                    var entry = raw[dateStr];
                    ParseDayPayload(dateStr, entry.Payload!, derived);
                    state.Entries[dateStr] = new FrontierJournalParseDayState
                    {
                        Status = "parsed_ok",
                        ParsedVersion = CurrentParseVersion,
                        ParsedAt = DateTime.UtcNow,
                        Error = null,
                    };
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "[FrontierJournalParse] Erreur date={Date}", dateStr);
                    state.Entries[dateStr] = new FrontierJournalParseDayState
                    {
                        Status = "parsed_error",
                        ParsedVersion = CurrentParseVersion,
                        ParsedAt = DateTime.UtcNow,
                        Error = ex.Message,
                    };
                    state.LastBatchError = $"{dateStr}: {ex.Message}";
                }
            }

            derived.ParseVersion = CurrentParseVersion;
            derived.UpdatedAt = DateTime.UtcNow;
            SaveDerived(derived);
            SaveParseState(state);
            _log.LogInformation("[FrontierJournalParse] Lot terminé : {Count} jour(s) traité(s), {Sys} système(s)", datesToProcess.Count, derived.Systems.Count);
            if (datesToProcess.Count > 0)
                LogDerivedDiagnostics(derived);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierJournalParse] Erreur fatale lot");
            var state = LoadParseState() ?? new FrontierJournalParseState { Entries = new Dictionary<string, FrontierJournalParseDayState>() };
            state.LastBatchError = ex.Message;
            SaveParseState(state);
        }
        finally
        {
            lock (_runLock)
            {
                _parseTask = null;
            }
        }
    }

    private void LogDerivedDiagnostics(FrontierJournalDerivedFile derived)
    {
        IEnumerable<FrontierJournalDerivedSystem> systems = derived.Systems != null
            ? derived.Systems.Values
            : Enumerable.Empty<FrontierJournalDerivedSystem>();
        var visited = systems.Count(s => s.IsVisited);
        var fullScanned = systems.Count(s => s.IsFullScanned);
        var hasFirstDiscoveryBody = systems.Count(s => s.HasFirstDiscoveryBody);
        var withCoords = systems.Count(s => s.CoordsX.HasValue && s.CoordsY.HasValue && s.CoordsZ.HasValue);
        _log.LogInformation(
            "[FrontierJournalParse] Résumé agrégats — visited={Visited}, fullScanned={FullScanned}, hasFirstDiscoveryBody={First}, withCoords={Coords}, systèmesDistincts={Total}",
            visited,
            fullScanned,
            hasFirstDiscoveryBody,
            withCoords,
            derived.Systems?.Count ?? 0);

        var examples = systems
            .Where(s => s.IsVisited || s.IsFullScanned || s.HasFirstDiscoveryBody)
            .OrderByDescending(s => s.HasFirstDiscoveryBody)
            .ThenByDescending(s => s.IsFullScanned)
            .ThenBy(s => s.SystemName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        foreach (var ex in examples)
        {
            var cx = ex.CoordsX.HasValue ? ex.CoordsX.Value.ToString("F2", CultureInfo.InvariantCulture) : "—";
            var cy = ex.CoordsY.HasValue ? ex.CoordsY.Value.ToString("F2", CultureInfo.InvariantCulture) : "—";
            var cz = ex.CoordsZ.HasValue ? ex.CoordsZ.Value.ToString("F2", CultureInfo.InvariantCulture) : "—";
            _log.LogInformation(
                "[FrontierJournalParse] Exemple — {Name} | coords=({Cx},{Cy},{Cz}) | visited={V} fullScan={F} firstDiscoveryBody={D}",
                ex.SystemName,
                cx,
                cy,
                cz,
                ex.IsVisited,
                ex.IsFullScanned,
                ex.HasFirstDiscoveryBody);
        }
    }

    private static bool NeedsParsing(string dateStr, FrontierJournalParseState state)
    {
        if (!state.Entries.TryGetValue(dateStr, out var day))
            return true;
        if (day.Status == "parsed_error")
            return true;
        if (day.Status == "parsed_ok" && day.ParsedVersion < CurrentParseVersion)
            return true;
        return false;
    }

    private static int CountPendingDates(HashSet<string> rawDates, FrontierJournalParseState? state)
    {
        var st = state?.Entries;
        return rawDates.Count(d =>
        {
            if (st == null || !st.TryGetValue(d, out var day))
                return true;
            if (day.Status == "parsed_error")
                return true;
            if (day.Status == "parsed_ok" && day.ParsedVersion < CurrentParseVersion)
                return true;
            return false;
        });
    }

    private void ParseDayPayload(string dateStr, string payload, FrontierJournalDerivedFile derived)
    {
        var trimmed = payload.TrimStart();
        if (trimmed.Length == 0)
            return;

        if (trimmed[0] == '[')
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object)
                    ProcessJournalEvent(dateStr, el, derived);
            }
            return;
        }

        foreach (var rawLine in payload.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    ProcessJournalEvent(dateStr, doc.RootElement, derived);
            }
            catch (JsonException)
            {
                /* ligne non JSON — ignorée */
            }
        }
    }

    private void ProcessJournalEvent(string dateStr, JsonElement el, FrontierJournalDerivedFile derived)
    {
        if (!el.TryGetProperty("event", out var evProp))
            return;
        var eventName = evProp.GetString();
        if (string.IsNullOrEmpty(eventName))
            return;
        if (!el.TryGetProperty("timestamp", out var tsProp))
            return;
        var ts = ParseTimestamp(tsProp.GetString());

        switch (eventName)
        {
            case "FSDJump":
            case "CarrierJump":
            case "Location":
                {
                    var starSystem = GetStarSystem(el);
                    if (string.IsNullOrWhiteSpace(starSystem))
                        return;
                    var sys = GetOrCreateSystem(derived, starSystem);
                    ApplyVisit(sys, ts, $"{eventName}:{dateStr}");
                    TryApplyStarPos(el, sys);
                    break;
                }
            case "FSSAllBodiesFound":
                {
                    var name = GetStarSystem(el);
                    if (string.IsNullOrWhiteSpace(name))
                        return;
                    var sys = GetOrCreateSystem(derived, name);
                    sys.IsFullScanned = true;
                    sys.LastProvenance = $"FSSAllBodiesFound:{dateStr}";
                    TryApplyStarPos(el, sys);
                    break;
                }
            case "Scan":
                {
                    if (!el.TryGetProperty("WasDiscovered", out var wd) || wd.ValueKind != JsonValueKind.False)
                        return;
                    var scanSys = GetStarSystem(el);
                    if (string.IsNullOrWhiteSpace(scanSys))
                        return;
                    var sys = GetOrCreateSystem(derived, scanSys);
                    sys.HasFirstDiscoveryBody = true;
                    sys.LastProvenance = $"Scan:FirstDiscovery:{dateStr}";
                    break;
                }
            default:
                break;
        }
    }

    private static FrontierJournalDerivedSystem GetOrCreateSystem(FrontierJournalDerivedFile derived, string starSystem)
    {
        var key = NormalizeSystemKey(starSystem);
        if (!derived.Systems!.TryGetValue(key, out var sys))
        {
            sys = new FrontierJournalDerivedSystem
            {
                SystemName = starSystem.Trim(),
            };
            derived.Systems[key] = sys;
        }
        return sys;
    }

    private static void ApplyVisit(FrontierJournalDerivedSystem sys, DateTime ts, string provenance)
    {
        sys.IsVisited = true;
        sys.VisitCount++;
        if (sys.FirstVisitedAt == null || ts < sys.FirstVisitedAt.Value)
            sys.FirstVisitedAt = ts;
        if (sys.LastVisitedAt == null || ts > sys.LastVisitedAt.Value)
            sys.LastVisitedAt = ts;
        sys.LastProvenance = provenance;
    }

    private static DateTime ParseTimestamp(string? iso)
    {
        if (string.IsNullOrEmpty(iso))
            return DateTime.UtcNow;
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        return DateTime.UtcNow;
    }

    /// <summary>StarSystem (sauts, Location, Scan) ou SystemName (ex. FSSAllBodiesFound).</summary>
    private static string? GetStarSystem(JsonElement el)
    {
        if (el.TryGetProperty("StarSystem", out var ss) && ss.ValueKind == JsonValueKind.String)
            return ss.GetString();
        if (el.TryGetProperty("SystemName", out var sn) && sn.ValueKind == JsonValueKind.String)
            return sn.GetString();
        return null;
    }

    /// <summary>Coordonnées galactiques (Ly) : StarPos ou Position [x,y,z] depuis le journal.</summary>
    private static void TryApplyStarPos(JsonElement el, FrontierJournalDerivedSystem sys)
    {
        if (TryReadVec3(el, "StarPos", out var x, out var y, out var z) ||
            TryReadVec3(el, "Position", out x, out y, out z))
        {
            sys.CoordsX = x;
            sys.CoordsY = y;
            sys.CoordsZ = z;
        }
    }

    private static bool TryReadVec3(JsonElement el, string propertyName, out double x, out double y, out double z)
    {
        x = y = z = 0;
        if (!el.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return false;
        using var en = arr.EnumerateArray();
        if (!en.MoveNext()) return false;
        var ex = en.Current;
        if (!en.MoveNext()) return false;
        var ey = en.Current;
        if (!en.MoveNext()) return false;
        var ez = en.Current;
        return ex.TryGetDouble(out x) && ey.TryGetDouble(out y) && ez.TryGetDouble(out z);
    }

    private static string NormalizeSystemKey(string name) => name.Trim().ToUpperInvariant();

    private Dictionary<string, FrontierJournalRawEntry> LoadRawDictionary()
    {
        var path = Path.Combine(DataDir, RawFileName);
        if (!File.Exists(path))
            return new Dictionary<string, FrontierJournalRawEntry>();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, FrontierJournalRawEntry>>(json)
                   ?? new Dictionary<string, FrontierJournalRawEntry>();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[FrontierJournalParse] Lecture raw échouée");
            return new Dictionary<string, FrontierJournalRawEntry>();
        }
    }

    private HashSet<string> LoadRawKeys()
    {
        var raw = LoadRawDictionary();
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kv in raw)
        {
            if (string.Equals(kv.Value.Status, "success", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(kv.Value.Payload)
                && kv.Value.Payload!.Trim() != "[]")
                set.Add(kv.Key);
        }
        return set;
    }

    private FrontierJournalParseState? LoadParseState()
    {
        var path = Path.Combine(DataDir, ParseStateFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FrontierJournalParseState>(json);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[FrontierJournalParse] Lecture parse-state échouée");
            return null;
        }
    }

    private void SaveParseState(FrontierJournalParseState state)
    {
        var path = Path.Combine(DataDir, ParseStateFileName);
        var tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(DataDir);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierJournalParse] Écriture parse-state échouée");
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private FrontierJournalDerivedFile? LoadDerived()
    {
        var path = Path.Combine(DataDir, DerivedFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FrontierJournalDerivedFile>(json);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[FrontierJournalParse] Lecture derived échouée");
            return null;
        }
    }

    private void SaveDerived(FrontierJournalDerivedFile derived)
    {
        var path = Path.Combine(DataDir, DerivedFileName);
        var tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(DataDir);
            var json = JsonSerializer.Serialize(derived, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierJournalParse] Écriture derived échouée");
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}

public class FrontierJournalParseState
{
    public Dictionary<string, FrontierJournalParseDayState> Entries { get; set; } = new();
    public string? LastBatchError { get; set; }
}

public class FrontierJournalParseDayState
{
    public string Status { get; set; } = ""; // not_parsed | parsed_ok | parsed_error
    public int ParsedVersion { get; set; }
    public DateTime? ParsedAt { get; set; }
    public string? Error { get; set; }
}

public class FrontierJournalDerivedFile
{
    public int ParseVersion { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Dictionary<string, FrontierJournalDerivedSystem> Systems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class FrontierJournalDerivedSystem
{
    public string SystemName { get; set; } = "";
    public DateTime? FirstVisitedAt { get; set; }
    public DateTime? LastVisitedAt { get; set; }
    public int VisitCount { get; set; }
    public bool IsVisited { get; set; }
    /// <summary>Au moins un corps du système avec Scan et WasDiscovered=false (première découverte).</summary>
    public bool HasFirstDiscoveryBody { get; set; }
    public bool IsFullScanned { get; set; }
    public string? LastProvenance { get; set; }
    public double? CoordsX { get; set; }
    public double? CoordsY { get; set; }
    public double? CoordsZ { get; set; }
}

public class FrontierJournalParseStatusDto
{
    public bool IsRunning { get; set; }
    public int ParseVersion { get; set; }
    public int PendingDaysEstimate { get; set; }
    public int ParsedDaysCount { get; set; }
    public int ErrorDaysCount { get; set; }
    public int SystemsCount { get; set; }
    public DateTime? DerivedUpdatedAt { get; set; }
    public string? LastParseError { get; set; }
}

public class FrontierJournalDerivedResponseDto
{
    public int ParseVersion { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<FrontierJournalSystemDerivedDto> Systems { get; set; } = new();
}

public class FrontierJournalSystemDerivedDto
{
    public string SystemName { get; set; } = "";
    public DateTime? FirstVisitedAt { get; set; }
    public DateTime? LastVisitedAt { get; set; }
    public int VisitCount { get; set; }
    public bool IsVisited { get; set; }
    public bool HasFirstDiscoveryBody { get; set; }
    public bool IsFullScanned { get; set; }
    public string? Provenance { get; set; }
    public double? CoordsX { get; set; }
    public double? CoordsY { get; set; }
    public double? CoordsZ { get; set; }
}
