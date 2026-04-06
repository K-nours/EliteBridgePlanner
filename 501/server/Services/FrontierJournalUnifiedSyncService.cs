namespace GuildDashboard.Server.Services;

/// <summary>
/// Orchestration unique : téléchargement journal Frontier (dates manquantes) puis parsing incrémental, par CMDR.
/// </summary>
public sealed class FrontierJournalUnifiedSyncService
{
    private readonly FrontierJournalBackfillService _backfill;
    private readonly FrontierJournalParseService _parse;
    private readonly FrontierTokenStore _tokens;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FrontierJournalUnifiedSyncService> _log;

    private readonly object _gate = new();
    private Task? _task;
    private CancellationTokenSource? _cts;
    private FrontierJournalUnifiedSyncStatusDto _status = new();

    public FrontierJournalUnifiedSyncService(
        FrontierJournalBackfillService backfill,
        FrontierJournalParseService parse,
        FrontierTokenStore tokens,
        IServiceScopeFactory scopeFactory,
        ILogger<FrontierJournalUnifiedSyncService> log)
    {
        _backfill = backfill;
        _parse = parse;
        _tokens = tokens;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public FrontierJournalUnifiedSyncStatusDto GetStatusSnapshot()
    {
        lock (_gate)
        {
            return new FrontierJournalUnifiedSyncStatusDto
            {
                Phase = _status.Phase,
                IsRunning = _status.IsRunning,
                FrontierCustomerId = _status.FrontierCustomerId,
                CommanderName = _status.CommanderName,
                LastSyncCompletedUtc = _status.LastSyncCompletedUtc,
                LastMessage = _status.LastMessage,
                SummaryMessage = _status.SummaryMessage,
                LastError = _status.LastError,
                FrontierSessionUxAction = _status.FrontierSessionUxAction,
                FetchedSuccessDaysApprox = _status.FetchedSuccessDaysApprox,
                DaysParsedThisRun = _status.DaysParsedThisRun,
                NewDaysFetchedThisRun = _status.NewDaysFetchedThisRun,
                PendingParseDays = _status.PendingParseDays,
                SystemsWithCoordsCount = _status.SystemsWithCoordsCount,
            };
        }
    }

    /// <summary>Démarre le pipeline complet (false si déjà en cours).</summary>
    public bool Start()
    {
        lock (_gate)
        {
            if (_task != null && !_task.IsCompleted)
                return false;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _status = new FrontierJournalUnifiedSyncStatusDto
            {
                Phase = "starting",
                IsRunning = true,
                LastMessage = "Journal Frontier : synchronisation démarrée",
                SummaryMessage = null,
                FrontierSessionUxAction = null,
            };
            _task = RunPipelineAsync(ct);
            return true;
        }
    }

    public bool Stop()
    {
        lock (_gate)
        {
            if (_cts == null || _cts.IsCancellationRequested) return false;
            _cts.Cancel();
            _log.LogInformation("[FrontierJournalUnified] Arrêt demandé");
            return true;
        }
    }

    /// <summary>Appelé après OAuth Frontier réussi : retire l’état d’erreur « connecter / reconnecter » si aucune synchro en cours.</summary>
    public void ClearAuthErrorStateAfterFrontierLogin()
    {
        lock (_gate)
        {
            if (_task != null && !_task.IsCompleted) return;
            if (_status.Phase != "error") return;
            var a = _status.FrontierSessionUxAction;
            if (a is not ("login" or "relogin")) return;
            _status = new FrontierJournalUnifiedSyncStatusDto
            {
                Phase = "idle",
                IsRunning = false,
            };
        }
    }

    private void MutateStatus(Action<FrontierJournalUnifiedSyncStatusDto> fn)
    {
        lock (_gate)
        {
            fn(_status);
        }
    }

    private void ReportProgress(string message)
    {
        MutateStatus(s => s.LastMessage = message);
    }

    /// <summary>
    /// Garantit un access token utilisable (refresh si besoin). Sinon applique l’état d’erreur UX et retourne false.
    /// </summary>
    private async Task<bool> EnsureFrontierAccessForJournalAsync(CancellationToken ct)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var oauth = scope.ServiceProvider.GetRequiredService<FrontierOAuthSessionService>();
            await oauth.GetEffectiveTokenAsync(ct);
        }

        var diag = _tokens.GetSessionDiagnostics();

        if (!diag.HasStoredToken || (!diag.HasAccessToken && !diag.HasRefreshToken))
        {
            ApplyJournalAuthUxError(
                "not_connected",
                "Journal Frontier : non connecté — veuillez connecter votre compte Frontier",
                "login");
            return false;
        }

        var mustRefresh = !diag.HasAccessToken || diag.AccessProbablyExpired;
        if (mustRefresh)
        {
            if (!diag.HasRefreshToken)
            {
                ApplyJournalAuthUxError(
                    "session_expired",
                    "Journal Frontier : session expirée — veuillez reconnecter Frontier",
                    "relogin");
                return false;
            }

            using (var scope = _scopeFactory.CreateScope())
            {
                var auth = scope.ServiceProvider.GetRequiredService<FrontierAuthService>();
                var t = _tokens.GetToken();
                if (t == null)
                {
                    ApplyJournalAuthUxError(
                        "not_connected",
                        "Journal Frontier : non connecté — veuillez connecter votre compte Frontier",
                        "login");
                    return false;
                }

                var refreshed = await auth.RefreshTokenAsync(t.RefreshToken ?? "", ct);
                if (refreshed == null)
                {
                    _log.LogWarning("[FrontierJournalUnified] Refresh Frontier échoué — session à renouveler");
                    ApplyJournalAuthUxError(
                        "session_expired",
                        "Journal Frontier : session expirée — veuillez reconnecter Frontier",
                        "relogin");
                    return false;
                }

                var oauthPersist = scope.ServiceProvider.GetRequiredService<FrontierOAuthSessionService>();
                await oauthPersist.PersistAndSetAsync(refreshed, null, ct);
            }
        }

        var after = _tokens.GetToken();
        if (after == null || string.IsNullOrEmpty(after.AccessToken))
        {
            ApplyJournalAuthUxError(
                "session_expired",
                "Journal Frontier : session expirée — veuillez reconnecter Frontier",
                "relogin");
            return false;
        }

        return true;
    }

    private void ApplyJournalAuthUxError(string lastErrorCode, string lastMessage, string frontierSessionUxAction)
    {
        MutateStatus(s =>
        {
            s.Phase = "error";
            s.IsRunning = false;
            s.LastError = lastErrorCode;
            s.LastMessage = lastMessage;
            s.FrontierSessionUxAction = frontierSessionUxAction;
        });
    }

    private async Task RunPipelineAsync(CancellationToken ct)
    {
        string commanderId = "";
        string commanderName = "";
        try
        {
            await Task.Yield();

            if (!await EnsureFrontierAccessForJournalAsync(ct))
                return;

            using (var scope = _scopeFactory.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<FrontierUserService>();
                var profile = await users.GetProfileAsync(ct);
                if (profile == null || string.IsNullOrEmpty(profile.FrontierCustomerId))
                {
                    MutateStatus(s =>
                    {
                        s.Phase = "error";
                        s.IsRunning = false;
                        s.LastError = "profile_unavailable";
                        s.LastMessage = "Journal Frontier : erreur lors de la récupération des données";
                        s.FrontierSessionUxAction = null;
                    });
                    return;
                }

                commanderId = profile.FrontierCustomerId;
                commanderName = profile.CommanderName ?? "";
            }

            var cid = commanderId;
            var cname = commanderName;
            MutateStatus(s =>
            {
                s.FrontierCustomerId = cid;
                s.CommanderName = cname;
                s.Phase = "fetching";
                s.LastMessage = "Journal Frontier : connexion et préparation du téléchargement";
                s.LastError = null;
                s.FrontierSessionUxAction = null;
                s.NewDaysFetchedThisRun = 0;
            });

            var daysBeforeFetch = _backfill.CountFetchedSuccessDays(commanderId);
            await _backfill.RunForCommanderAsync(commanderId, recentDays: null, ct, ReportProgress);
            var daysAfterFetch = _backfill.CountFetchedSuccessDays(commanderId);
            var newDaysFetched = Math.Max(0, daysAfterFetch - daysBeforeFetch);

            MutateStatus(s =>
            {
                s.Phase = "parsing";
                s.LastMessage = "Journal Frontier : parsing des nouvelles dates";
                s.FetchedSuccessDaysApprox = daysAfterFetch;
                s.NewDaysFetchedThisRun = newDaysFetched;
            });

            var parsedTotal = await _parse.ParseAllPendingAsync(commanderId, 80, ct);

            MutateStatus(s => { s.LastMessage = "Journal Frontier : agrégats mis à jour"; });

            var parseSt = _parse.GetParseStatus(commanderId);
            var summary =
                $"Journal Frontier : {newDaysFetched} nouvelle(s) date(s) récupérée(s), {parsedTotal} jour(s) parsé(s), " +
                $"agrégat actualisé — {parseSt.SystemsCount} système(s), {parseSt.SystemsWithCoordsCount} avec coords pour la carte";
            MutateStatus(s =>
            {
                s.Phase = "completed";
                s.IsRunning = false;
                s.DaysParsedThisRun = parsedTotal;
                s.PendingParseDays = parseSt.PendingDaysEstimate;
                s.SystemsWithCoordsCount = parseSt.SystemsWithCoordsCount;
                s.FetchedSuccessDaysApprox = _backfill.CountFetchedSuccessDays(commanderId);
                s.LastSyncCompletedUtc = DateTime.UtcNow;
                s.LastMessage = "Journal Frontier : terminé";
                s.SummaryMessage = summary;
                s.LastError = null;
                s.FrontierSessionUxAction = null;
            });
        }
        catch (OperationCanceledException)
        {
            MutateStatus(s =>
            {
                s.Phase = "idle";
                s.IsRunning = false;
                s.LastMessage = "Journal Frontier : synchronisation interrompue";
                s.FrontierSessionUxAction = null;
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierJournalUnified] Erreur pipeline");
            MutateStatus(s =>
            {
                s.Phase = "error";
                s.IsRunning = false;
                s.LastError = ex.Message;
                s.LastMessage = "Journal Frontier : erreur lors de la récupération des données";
                s.FrontierSessionUxAction = null;
            });
        }
        finally
        {
            lock (_gate)
            {
                _task = null;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }
}

public sealed class FrontierJournalUnifiedSyncStatusDto
{
    /// <summary>idle | starting | fetching | parsing | completed | error</summary>
    public string Phase { get; set; } = "idle";

    public bool IsRunning { get; set; }
    public string? FrontierCustomerId { get; set; }
    public string? CommanderName { get; set; }
    public DateTime? LastSyncCompletedUtc { get; set; }
    public string? LastMessage { get; set; }
    /// <summary>Récap chiffré affiché à la fin du run (logs / statut).</summary>
    public string? SummaryMessage { get; set; }
    public string? LastError { get; set; }
    /// <summary>login | relogin — bouton « Connecter / Reconnecter Frontier » côté UI.</summary>
    public string? FrontierSessionUxAction { get; set; }
    public int FetchedSuccessDaysApprox { get; set; }
    public int DaysParsedThisRun { get; set; }
    /// <summary>Nouveaux jours « success » dans le brut après ce run (approx. succès CAPI).</summary>
    public int NewDaysFetchedThisRun { get; set; }
    public int PendingParseDays { get; set; }
    public int SystemsWithCoordsCount { get; set; }
}
