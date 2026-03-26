using System.Globalization;
using System.Text.Json;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Téléchargement incrémental du journal Frontier CAPI par date UTC, pour un CMDR donné (dossier dédié).
/// Les jours déjà en success/empty dans le brut local sont ignorés ; les erreurs peuvent être re-téléchargées au prochain run.
/// </summary>
public class FrontierJournalBackfillService
{
    private static readonly DateTime MinDate = new(2021, 1, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan PauseBetweenDays = TimeSpan.FromSeconds(5);

    private readonly FrontierTokenStore _tokenStore;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FrontierJournalBackfillService> _log;

    public const string RawFileName = "frontier-journal-raw.json";
    private const string ProgressFileName = "frontier-journal-progress.json";
    private const string LogFileName = "frontier-journal-log.json";
    private const string CapiJournalBase = "https://companion.orerve.net";

    public FrontierJournalBackfillService(
        FrontierTokenStore tokenStore,
        IHttpClientFactory httpFactory,
        IWebHostEnvironment env,
        ILogger<FrontierJournalBackfillService> log)
    {
        _tokenStore = tokenStore;
        _httpFactory = httpFactory;
        _env = env;
        _log = log;
    }

    private static DateTime FloorFromProgress(FrontierJournalProgress? p)
    {
        if (string.IsNullOrEmpty(p?.EffectiveMinDate))
            return MinDate;
        return DateTime.TryParse(p.EffectiveMinDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Date
            : MinDate;
    }

    /// <summary>Exécution attendue par l’orchestrateur (sync complète fetch + annulation via <paramref name="ct"/>).</summary>
    /// <param name="progressReporter">Mise à jour libellé UI (thread-safe attendu côté appelant si besoin).</param>
    public async Task RunForCommanderAsync(
        string frontierCustomerId,
        int? recentDays,
        CancellationToken ct,
        Action<string>? progressReporter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierCustomerId);
        var token = _tokenStore.GetToken();
        if (token == null || string.IsNullOrEmpty(token.AccessToken))
            throw new InvalidOperationException("FRONTIER_JOURNAL_NO_ACCESS_TOKEN");

        var journalDir = FrontierJournalStoragePaths.CommanderDirectory(_env, frontierCustomerId);
        FrontierJournalStoragePaths.TryMigrateLegacyJournalFiles(_env, journalDir, _log);
        Directory.CreateDirectory(journalDir);

        await RunBackfillAsync(token.AccessToken, journalDir, recentDays, ct, progressReporter);
    }

    /// <summary>État persistant du fetch (fichier progress du CMDR).</summary>
    public FrontierJournalBackfillStatus GetStatus(string frontierCustomerId)
    {
        var journalDir = FrontierJournalStoragePaths.CommanderDirectory(_env, frontierCustomerId);
        var progress = LoadProgress(journalDir);
        return new FrontierJournalBackfillStatus
        {
            IsRunning = false,
            Completed = progress?.Completed ?? false,
            CurrentDate = progress?.CurrentDate,
            StartDate = progress?.StartDate,
            MinDate = progress?.MinDate ?? MinDate.ToString("yyyy-MM-dd"),
            EffectiveMinDate = progress?.EffectiveMinDate,
            TotalDaysProcessed = progress?.TotalDaysProcessed ?? 0,
            SuccessCount = progress?.SuccessCount ?? 0,
            EmptyCount = progress?.EmptyCount ?? 0,
            ErrorCount = progress?.ErrorCount ?? 0,
            StartedAt = progress?.StartedAt,
            UpdatedAt = progress?.UpdatedAt,
        };
    }

    /// <summary>Nombre de clés « success » avec payload non vide dans le brut.</summary>
    public int CountFetchedSuccessDays(string frontierCustomerId)
    {
        var journalDir = FrontierJournalStoragePaths.CommanderDirectory(_env, frontierCustomerId);
        var raw = LoadRaw(journalDir);
        return raw.Count(kv =>
            string.Equals(kv.Value.Status, "success", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(kv.Value.Payload)
            && kv.Value.Payload.Trim() != "[]");
    }

    private static int CountMissingJournalDates(DateTime current, DateTime floorDate, Dictionary<string, FrontierJournalRawEntry> raw)
    {
        var n = 0;
        for (var d = current.Date; d >= floorDate.Date; d = d.AddDays(-1))
        {
            var ds = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!raw.TryGetValue(ds, out var ex))
            {
                n++;
                continue;
            }
            var done = string.Equals(ex.Status, "success", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(ex.Status, "empty", StringComparison.OrdinalIgnoreCase);
            if (!done) n++;
        }
        return n;
    }

    private async Task RunBackfillAsync(
        string accessToken,
        string journalDir,
        int? recentDaysForNewStart,
        CancellationToken ct,
        Action<string>? progressReporter)
    {
        try
        {
            var progress = LoadProgress(journalDir);
            DateTime current;
            string startDateStr;

            var resume = progress != null && !progress.Completed;
            if (resume && recentDaysForNewStart is >= 1 and <= 366 && string.IsNullOrEmpty(progress!.EffectiveMinDate))
                resume = false;

            if (resume)
            {
                current = DateTime.Parse(progress!.CurrentDate!, CultureInfo.InvariantCulture).Date;
                startDateStr = progress.StartDate!;
                AppendLog(journalDir, "resume", current.ToString("yyyy-MM-dd"), $"Reprise depuis {current:yyyy-MM-dd}");
                _log.LogInformation("[FrontierJournal] RESUME from={Date}", current.ToString("yyyy-MM-dd"));
            }
            else
            {
                current = DateTime.UtcNow.Date.AddDays(-1);
                startDateStr = current.ToString("yyyy-MM-dd");
                var floorBoundary = recentDaysForNewStart is >= 1 and <= 366
                    ? DateTime.UtcNow.Date.AddDays(-recentDaysForNewStart.Value)
                    : MinDate;
                progress = new FrontierJournalProgress
                {
                    CurrentDate = current.ToString("yyyy-MM-dd"),
                    StartDate = startDateStr,
                    MinDate = floorBoundary.ToString("yyyy-MM-dd"),
                    EffectiveMinDate = recentDaysForNewStart is >= 1 and <= 366 ? floorBoundary.ToString("yyyy-MM-dd") : null,
                    TotalDaysProcessed = 0,
                    SuccessCount = 0,
                    EmptyCount = 0,
                    ErrorCount = 0,
                    StartedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Completed = false,
                };
                var hint = recentDaysForNewStart is >= 1 and <= 366
                    ? $" fenêtre={recentDaysForNewStart}j min={floorBoundary:yyyy-MM-dd}"
                    : "";
                AppendLog(journalDir, "start", current.ToString("yyyy-MM-dd"), $"Démarrage backfill date={current:yyyy-MM-dd}{hint}");
                _log.LogInformation("[FrontierJournal] START date={Date}{Hint}", current.ToString("yyyy-MM-dd"), hint);
            }

            var floorDate = FloorFromProgress(progress);
            var raw = LoadRaw(journalDir);

            progressReporter?.Invoke("Journal Frontier : vérification des dates déjà présentes");
            var missingUpFront = CountMissingJournalDates(current, floorDate, raw);
            progressReporter?.Invoke(missingUpFront == 0
                ? "Journal Frontier : aucune date manquante à télécharger"
                : $"Journal Frontier : {missingUpFront} date(s) manquante(s) détectée(s)");

            while (current >= floorDate && !ct.IsCancellationRequested)
            {
                var dateStr = current.ToString("yyyy-MM-dd");
                if (raw.TryGetValue(dateStr, out var existing) &&
                    (string.Equals(existing.Status, "success", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(existing.Status, "empty", StringComparison.OrdinalIgnoreCase)))
                {
                    progress!.CurrentDate = dateStr;
                    progress.UpdatedAt = DateTime.UtcNow;
                    _log.LogDebug("[FrontierJournal] SKIP date={Date} (déjà {Status})", dateStr, existing.Status);
                    if (current <= floorDate)
                    {
                        progress.Completed = true;
                        SaveProgress(journalDir, progress);
                        var finishMsg = $"FINISHED processed={progress.TotalDaysProcessed} success={progress.SuccessCount} empty={progress.EmptyCount} error={progress.ErrorCount}";
                        AppendLog(journalDir, "finished", dateStr, finishMsg);
                        break;
                    }
                    SaveProgress(journalDir, progress);
                    current = current.AddDays(-1);
                    continue;
                }

                progressReporter?.Invoke($"Journal Frontier : récupération de {dateStr}");
                var (statusCode, body) = await FetchJournalDayAsync(accessToken, current.Year, current.Month, current.Day, ct);
                var fetchedAt = DateTime.UtcNow;
                int? entriesCount = null;

                if (statusCode is >= 200 and < 300 && !string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                            entriesCount = doc.RootElement.GetArrayLength();
                    }
                    catch { /* ignore */ }
                }

                string status;
                string logType;
                string logMessage;
                if (statusCode is >= 200 and < 300)
                {
                    if (string.IsNullOrWhiteSpace(body) || body.Trim() == "[]")
                    {
                        status = "empty";
                        logType = "empty";
                        logMessage = $"EMPTY date={dateStr}";
                        progress!.EmptyCount++;
                    }
                    else
                    {
                        status = "success";
                        logType = "success";
                        logMessage = $"SUCCESS date={dateStr} entries={entriesCount ?? -1}";
                        progress!.SuccessCount++;
                    }
                }
                else
                {
                    status = "error";
                    logType = "error";
                    logMessage = $"ERROR date={dateStr} status={statusCode}";
                    progress!.ErrorCount++;
                }

                var rawEntry = new FrontierJournalRawEntry
                {
                    RequestedDate = dateStr,
                    Status = status,
                    Payload = body,
                    FetchedAt = fetchedAt,
                    PayloadSize = body?.Length ?? 0,
                    HttpStatusCode = statusCode,
                    EntriesCount = entriesCount,
                };

                raw[dateStr] = rawEntry;
                progress!.TotalDaysProcessed++;
                progress.CurrentDate = dateStr;
                progress.UpdatedAt = fetchedAt;

                AppendLog(journalDir, logType, dateStr, logMessage, statusCode, body?.Length);
                _log.LogInformation("[FrontierJournal] {Msg}", logMessage);

                if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                    progressReporter?.Invoke($"Journal Frontier : {dateStr} — date récupérée");
                else if (string.Equals(status, "empty", StringComparison.OrdinalIgnoreCase))
                    progressReporter?.Invoke($"Journal Frontier : {dateStr} — jour sans entrée");
                else
                    progressReporter?.Invoke($"Journal Frontier : {dateStr} — échec de récupération");

                SaveRawPrivate(journalDir, raw);
                SaveProgress(journalDir, progress);

                if (current <= floorDate)
                {
                    progress.Completed = true;
                    progress.UpdatedAt = DateTime.UtcNow;
                    SaveProgress(journalDir, progress);
                    var finishMsg = $"FINISHED processed={progress.TotalDaysProcessed} success={progress.SuccessCount} empty={progress.EmptyCount} error={progress.ErrorCount}";
                    AppendLog(journalDir, "finished", dateStr, finishMsg);
                    _log.LogInformation("[FrontierJournal] {Msg}", finishMsg);
                    break;
                }

                current = current.AddDays(-1);
                await Task.Delay(PauseBetweenDays, ct);
            }
        }
        catch (OperationCanceledException)
        {
            _log.LogInformation("[FrontierJournal] Backfill annulé");
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierJournal] Erreur fatale du backfill");
            throw;
        }
    }

    private void SaveRawPrivate(string journalDir, Dictionary<string, FrontierJournalRawEntry> raw)
    {
        var path = Path.Combine(journalDir, RawFileName);
        var tempPath = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierJournal] Erreur écriture raw");
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    private async Task<(int StatusCode, string? Body)> FetchJournalDayAsync(string accessToken, int year, int month, int day, CancellationToken ct)
    {
        var url = $"{CapiJournalBase}/journal/{year}/{month}/{day}";
        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        try
        {
            var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return ((int)response.StatusCode, body);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[FrontierJournal] Requête échouée: {Url}", url);
            return (0, null);
        }
    }

    public Dictionary<string, FrontierJournalRawEntry> LoadRaw(string journalDir)
    {
        var path = Path.Combine(journalDir, RawFileName);
        if (!File.Exists(path)) return new Dictionary<string, FrontierJournalRawEntry>();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, FrontierJournalRawEntry>>(json)
                   ?? new Dictionary<string, FrontierJournalRawEntry>();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[FrontierJournal] Erreur lecture raw, démarrage à vide");
            return new Dictionary<string, FrontierJournalRawEntry>();
        }
    }

    /// <summary>Persistance du brut local (même format que le backfill). Utilisé par l’import fusion.</summary>
    public void SaveRaw(string journalDir, Dictionary<string, FrontierJournalRawEntry> raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDir);
        SaveRawPrivate(journalDir, raw ?? new Dictionary<string, FrontierJournalRawEntry>());
    }

    public static FrontierJournalProgress? LoadProgressStatic(string journalDir)
    {
        var path = Path.Combine(journalDir, ProgressFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FrontierJournalProgress>(json);
        }
        catch
        {
            return null;
        }
    }

    private FrontierJournalProgress? LoadProgress(string journalDir) => LoadProgressStatic(journalDir);

    private void SaveProgress(string journalDir, FrontierJournalProgress progress)
    {
        var path = Path.Combine(journalDir, ProgressFileName);
        var tempPath = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierJournal] Erreur écriture progress");
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private void AppendLog(string journalDir, string type, string requestedDate, string message, int? httpStatusCode = null, int? payloadSize = null)
    {
        var path = Path.Combine(journalDir, LogFileName);
        var entry = new FrontierJournalLogEntry
        {
            Timestamp = DateTime.UtcNow,
            RequestedDate = requestedDate,
            Type = type,
            Message = message,
            HttpStatusCode = httpStatusCode,
            PayloadSize = payloadSize,
        };
        var line = JsonSerializer.Serialize(entry) + "\n";
        try
        {
            Directory.CreateDirectory(journalDir);
            File.AppendAllText(path, line);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierJournal] Erreur append log");
        }
    }
}

public class FrontierJournalRawEntry
{
    public string RequestedDate { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Payload { get; set; }
    public DateTime FetchedAt { get; set; }
    public int PayloadSize { get; set; }
    public int? HttpStatusCode { get; set; }
    public int? EntriesCount { get; set; }
}

public class FrontierJournalProgress
{
    public string? CurrentDate { get; set; }
    public string? StartDate { get; set; }
    public string? MinDate { get; set; }
    public string? EffectiveMinDate { get; set; }
    public int TotalDaysProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int EmptyCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool Completed { get; set; }
}

public class FrontierJournalLogEntry
{
    public DateTime Timestamp { get; set; }
    public string RequestedDate { get; set; } = "";
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public int? HttpStatusCode { get; set; }
    public int? PayloadSize { get; set; }
}

public class FrontierJournalBackfillStatus
{
    public bool IsRunning { get; set; }
    public bool Completed { get; set; }
    public string? CurrentDate { get; set; }
    public string? StartDate { get; set; }
    public string? MinDate { get; set; }
    public string? EffectiveMinDate { get; set; }
    public int TotalDaysProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int EmptyCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
