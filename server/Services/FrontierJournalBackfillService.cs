using System.Text.Json;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Backfill incrémental du journal Frontier CAPI par date.
/// Récupération brute jour par jour, persistance dans 3 fichiers JSON, reprise automatique.
/// </summary>
public class FrontierJournalBackfillService
{
    private static readonly DateTime MinDate = new(2021, 1, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan PauseBetweenDays = TimeSpan.FromSeconds(5);

    private readonly FrontierTokenStore _tokenStore;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FrontierJournalBackfillService> _log;

    private readonly object _runLock = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    private const string RawFileName = "frontier-journal-raw.json";
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

    private string DataDir => Path.Combine(_env.ContentRootPath ?? AppContext.BaseDirectory ?? ".", "Data", "frontier-journal");

    /// <summary>Retourne les dates à retraiter (status=error, HttpStatusCode=401) depuis raw.json.</summary>
    public IReadOnlyList<string> GetDatesToRetry()
    {
        var raw = LoadRaw();
        var list = new List<string>();
        foreach (var (dateStr, entry) in raw)
        {
            var status = entry.Status ?? "";
            var code = entry.HttpStatusCode ?? 0;
            if (status.Equals("error", StringComparison.OrdinalIgnoreCase) && code == 401)
                list.Add(dateStr);
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>Démarre le backfill (ou reprend si non terminé). Retourne false si déjà en cours.</summary>
    public bool Start()
    {
        lock (_runLock)
        {
            if (_runTask != null && !_runTask.IsCompleted)
            {
                _log.LogWarning("[FrontierJournal] Démarrage ignoré : backfill déjà en cours");
                return false;
            }

            var token = _tokenStore.GetToken();
            if (token == null || string.IsNullOrEmpty(token.AccessToken))
            {
                _log.LogWarning("[FrontierJournal] Impossible de démarrer : pas de token Frontier");
                return false;
            }

            _cts = new CancellationTokenSource();
            _runTask = RunBackfillAsync(token.AccessToken, _cts.Token);
            _log.LogInformation("[FrontierJournal] Backfill démarré");
            return true;
        }
    }

    /// <summary>Lance le retry uniquement sur les erreurs 401. Retourne false si déjà en cours ou aucune erreur à retraiter.</summary>
    public bool StartRetryErrors()
    {
        lock (_runLock)
        {
            if (_runTask != null && !_runTask.IsCompleted)
            {
                _log.LogWarning("[FrontierJournal] Retry ignoré : backfill déjà en cours");
                return false;
            }

            var datesToRetry = GetDatesToRetry();
            if (datesToRetry.Count == 0)
            {
                _log.LogInformation("[FrontierJournal] Retry : aucune erreur 401 à retraiter");
                return false;
            }

            var token = _tokenStore.GetToken();
            if (token == null || string.IsNullOrEmpty(token.AccessToken))
            {
                _log.LogWarning("[FrontierJournal] Retry impossible : pas de token Frontier (reconnectez-vous)");
                return false;
            }

            _cts = new CancellationTokenSource();
            _runTask = RunRetryErrorsAsync(token.AccessToken, datesToRetry, _cts.Token);
            _log.LogInformation("[FrontierJournal] Retry démarré : {Count} erreur(s) 401 à retraiter", datesToRetry.Count);
            return true;
        }
    }

    /// <summary>Arrête le backfill ou retry en cours. Retourne true si un job a été arrêté.</summary>
    public bool Stop()
    {
        lock (_runLock)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                _log.LogInformation("[FrontierJournal] Arrêt demandé");
                return true;
            }
            return false;
        }
    }

    /// <summary>Retourne l'état courant du backfill.</summary>
    public FrontierJournalBackfillStatus GetStatus()
    {
        var progress = LoadProgress();
        var isRunning = _runTask != null && !_runTask.IsCompleted;
        return new FrontierJournalBackfillStatus
        {
            IsRunning = isRunning,
            Completed = progress?.Completed ?? false,
            CurrentDate = progress?.CurrentDate,
            StartDate = progress?.StartDate,
            MinDate = progress?.MinDate ?? MinDate.ToString("yyyy-MM-dd"),
            TotalDaysProcessed = progress?.TotalDaysProcessed ?? 0,
            SuccessCount = progress?.SuccessCount ?? 0,
            EmptyCount = progress?.EmptyCount ?? 0,
            ErrorCount = progress?.ErrorCount ?? 0,
            StartedAt = progress?.StartedAt,
            UpdatedAt = progress?.UpdatedAt,
        };
    }

    private async Task RunBackfillAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var progress = LoadProgress();
            DateTime current;
            string startDateStr;
            DateTime minDate = MinDate;

            if (progress != null && !progress.Completed)
            {
                current = DateTime.Parse(progress.CurrentDate!).Date;
                startDateStr = progress.StartDate!;
                progress.TotalDaysProcessed = progress.TotalDaysProcessed;
                progress.SuccessCount = progress.SuccessCount;
                progress.EmptyCount = progress.EmptyCount;
                progress.ErrorCount = progress.ErrorCount;
                AppendLog("resume", current.ToString("yyyy-MM-dd"), $"Reprise depuis {current:yyyy-MM-dd}");
                _log.LogInformation("[FrontierJournal] RESUME from={Date}", current.ToString("yyyy-MM-dd"));
            }
            else
            {
                current = DateTime.UtcNow.Date.AddDays(-1);
                startDateStr = current.ToString("yyyy-MM-dd");
                progress = new FrontierJournalProgress
                {
                    CurrentDate = current.ToString("yyyy-MM-dd"),
                    StartDate = startDateStr,
                    MinDate = minDate.ToString("yyyy-MM-dd"),
                    TotalDaysProcessed = 0,
                    SuccessCount = 0,
                    EmptyCount = 0,
                    ErrorCount = 0,
                    StartedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Completed = false,
                };
                AppendLog("start", current.ToString("yyyy-MM-dd"), $"Démarrage backfill date={current:yyyy-MM-dd}");
                _log.LogInformation("[FrontierJournal] START date={Date}", current.ToString("yyyy-MM-dd"));
            }

            var raw = LoadRaw();
            var jsonOpts = new JsonSerializerOptions { WriteIndented = false };

            while (current >= minDate && !ct.IsCancellationRequested)
            {
                var dateStr = current.ToString("yyyy-MM-dd");
                if (raw.TryGetValue(dateStr, out var existing) &&
                    (string.Equals(existing.Status, "success", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(existing.Status, "empty", StringComparison.OrdinalIgnoreCase)))
                {
                    progress!.CurrentDate = dateStr;
                    progress.UpdatedAt = DateTime.UtcNow;
                    _log.LogInformation("[FrontierJournal] SKIP date={Date} (déjà {Status})", dateStr, existing.Status);
                    if (current <= minDate)
                    {
                        progress.Completed = true;
                        SaveProgress(progress);
                        var finishMsg = $"FINISHED processed={progress.TotalDaysProcessed} success={progress.SuccessCount} empty={progress.EmptyCount} error={progress.ErrorCount}";
                        AppendLog("finished", dateStr, finishMsg);
                        break;
                    }
                    SaveProgress(progress);
                    current = current.AddDays(-1);
                    continue;
                }

                var (statusCode, body) = await FetchJournalDayAsync(accessToken, current.Year, current.Month, current.Day, ct);
                var fetchedAt = DateTime.UtcNow;
                int? entriesCount = null;

                if (statusCode >= 200 && statusCode < 300)
                {
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                                entriesCount = doc.RootElement.GetArrayLength();
                        }
                        catch { /* ignore */ }
                    }
                }

                string status;
                string logType;
                string logMessage;
                if (statusCode >= 200 && statusCode < 300)
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

                AppendLog(logType, dateStr, logMessage, statusCode, body?.Length);
                _log.LogInformation("[FrontierJournal] {Msg}", logMessage);

                SaveRaw(raw);
                SaveProgress(progress);

                if (current <= minDate)
                {
                    progress.Completed = true;
                    progress.UpdatedAt = DateTime.UtcNow;
                    SaveProgress(progress);
                    var finishMsg = $"FINISHED processed={progress.TotalDaysProcessed} success={progress.SuccessCount} empty={progress.EmptyCount} error={progress.ErrorCount}";
                    AppendLog("finished", dateStr, finishMsg);
                    _log.LogInformation("[FrontierJournal] {Msg}", finishMsg);
                    break;
                }

                current = current.AddDays(-1);
                await Task.Delay(PauseBetweenDays, ct);
            }
        }
        catch (OperationCanceledException)
        {
            _log.LogInformation("[FrontierJournal] Backfill arrêté par l'utilisateur");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierJournal] Erreur fatale du backfill");
            AppendLog("error", "?", $"Exception: {ex.Message}", 0, null);
        }
        finally
        {
            lock (_runLock)
            {
                _runTask = null;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    private async Task RunRetryErrorsAsync(string accessToken, IReadOnlyList<string> datesToRetry, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var raw = LoadRaw();
            var progress = LoadProgress();

            var remaining = datesToRetry.Count;
            AppendLog("retry_start", datesToRetry[0], $"[Retry] démarré — {remaining} erreur(s) 401 à retraiter");
            _log.LogInformation("[FrontierJournal] RETRY démarré — {Count} dates à retraiter", remaining);

            foreach (var dateStr in datesToRetry)
            {
                if (ct.IsCancellationRequested) break;

                var parsed = DateTime.TryParse(dateStr, out var dt) ? dt : default;
                var (statusCode, body) = await FetchJournalDayAsync(accessToken, parsed.Year, parsed.Month, parsed.Day, ct);
                var fetchedAt = DateTime.UtcNow;
                int? entriesCount = null;

                if (statusCode == 401)
                {
                    AppendLog("retry_token_expired", dateStr, "Token expiré — backfill interrompu");
                    _log.LogWarning("[FrontierJournal] Token expiré — backfill interrompu. Reconnectez-vous puis relancez le retry.");
                    break;
                }

                if (statusCode >= 200 && statusCode < 300)
                {
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                                entriesCount = doc.RootElement.GetArrayLength();
                        }
                        catch { /* ignore */ }
                    }
                }

                if (statusCode >= 200 && statusCode < 300)
                {
                    var status = string.IsNullOrWhiteSpace(body) || body.Trim() == "[]" ? "empty" : "success";
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

                    if (progress != null)
                    {
                        progress.ErrorCount = Math.Max(0, progress.ErrorCount - 1);
                        if (status == "success")
                            progress.SuccessCount++;
                        else
                            progress.EmptyCount++;
                        progress.UpdatedAt = fetchedAt;
                    }

                    SaveRaw(raw);
                    if (progress != null) SaveProgress(progress);

                    AppendLog("retry_ok", dateStr, $"[Retry] date={dateStr} OK");
                    _log.LogInformation("[FrontierJournal] [Retry] date={Date} OK", dateStr);
                }
                else
                {
                    AppendLog("retry_failed", dateStr, $"[Retry] date={dateStr} FAILED status={statusCode}");
                    _log.LogWarning("[FrontierJournal] [Retry] date={Date} FAILED status={Status}", dateStr, statusCode);
                }

                remaining--;
                AppendLog("retry_remaining", dateStr, $"[Retry] remaining={remaining}");
                _log.LogInformation("[FrontierJournal] [Retry] remaining={Remaining}", remaining);

                if (remaining > 0)
                    await Task.Delay(PauseBetweenDays, ct);
            }

            var finishMsg = remaining == 0
                ? "[Retry] terminé — toutes les erreurs 401 retraitées"
                : $"[Retry] interrompu — {remaining} erreur(s) restante(s)";
            AppendLog("retry_finished", "", finishMsg);
            _log.LogInformation("[FrontierJournal] {Msg}", finishMsg);
        }
        catch (OperationCanceledException)
        {
            _log.LogInformation("[FrontierJournal] Retry arrêté");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierJournal] Erreur fatale du retry");
            AppendLog("error", "?", $"Retry exception: {ex.Message}", 0, null);
        }
        finally
        {
            lock (_runLock)
            {
                _runTask = null;
                _cts?.Dispose();
                _cts = null;
            }
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

    private Dictionary<string, FrontierJournalRawEntry> LoadRaw()
    {
        var path = Path.Combine(DataDir, RawFileName);
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

    private void SaveRaw(Dictionary<string, FrontierJournalRawEntry> raw)
    {
        var path = Path.Combine(DataDir, RawFileName);
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
        }
    }

    private FrontierJournalProgress? LoadProgress()
    {
        var path = Path.Combine(DataDir, ProgressFileName);
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

    private void SaveProgress(FrontierJournalProgress progress)
    {
        var path = Path.Combine(DataDir, ProgressFileName);
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

    private void AppendLog(string type, string requestedDate, string message, int? httpStatusCode = null, int? payloadSize = null)
    {
        var path = Path.Combine(DataDir, LogFileName);
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
            Directory.CreateDirectory(DataDir);
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
    public string Status { get; set; } = ""; // success | empty | error
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
    public int TotalDaysProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int EmptyCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
