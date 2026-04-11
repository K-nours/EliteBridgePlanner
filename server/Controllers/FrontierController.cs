using System.Diagnostics;
using GuildDashboard.Server.DTOs;
using GuildDashboard.Server.Models;
using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/integrations/frontier")]
public class FrontierController : ControllerBase
{
    private readonly FrontierAuthService _auth;
    private readonly FrontierTokenStore _store;
    private readonly FrontierOAuthSessionService _oauthSession;
    private readonly FrontierUserService _userService;
    private readonly FrontierJournalUnifiedSyncService _journalUnifiedSync;
    private readonly CurrentGuildService _currentGuild;
    private readonly DeclaredChantiersService _declaredChantiers;
    private readonly ILogger<FrontierController> _log;

    public FrontierController(
        FrontierAuthService auth,
        FrontierTokenStore store,
        FrontierOAuthSessionService oauthSession,
        FrontierUserService userService,
        FrontierJournalUnifiedSyncService journalUnifiedSync,
        CurrentGuildService currentGuild,
        DeclaredChantiersService declaredChantiers,
        ILogger<FrontierController> log)
    {
        _auth = auth;
        _store = store;
        _oauthSession = oauthSession;
        _userService = userService;
        _journalUnifiedSync = journalUnifiedSync;
        _currentGuild = currentGuild;
        _declaredChantiers = declaredChantiers;
        _log = log;
    }

    /// <summary>POST /api/integrations/frontier/logout — déconnexion Frontier (efface le token mémoire + session persistée).</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        _store.ClearToken();
        await _oauthSession.ClearPersistedAsync(ct);
        return Ok(new { success = true });
    }

    /// <summary>GET /api/integrations/frontier/me — état de connexion Frontier (connecté/non, commander, squadron).</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var profile = await _userService.GetProfileAsync(ct);
        if (profile == null)
            return Ok(new { connected = false });
        return Ok(new
        {
            connected = true,
            commander = profile.CommanderName,
            squadron = profile.SquadronName,
            customerId = profile.FrontierCustomerId,
        });
    }

    /// <summary>GET /api/integrations/frontier/profile — profil CMDR complet (CAPI), mappé squadron→guild.</summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var profile = await _userService.GetProfileAsync(ct);
        return profile == null ? NotFound(new { message = "Aucun profil Frontier (connexion OAuth requise)." }) : Ok(profile);
    }

    /// <summary>
    /// GET /api/integrations/frontier/chantiers-inspect — même CAPI /profile que le dashboard, analyse JSON pour debug chantier (pas de journal local, pas de nouvel OAuth).
    /// </summary>
    [HttpGet("chantiers-inspect")]
    public async Task<IActionResult> ChantiersInspect(CancellationToken ct)
    {
        var cachedBefore = await _userService.GetCachedProfileAsync(ct);
        var res = await _oauthSession.ResolveEffectiveTokenAsync(ct);
        var snap = _oauthSession.GetPersistenceSnapshot();
        var sessionDiag = _store.GetSessionDiagnostics();

        if (res.Token == null || string.IsNullOrEmpty(res.Token.AccessToken))
        {
            _log.LogInformation(
                "[FrontierChantiersInspect] Pas de token OAuth résolu — profil SQL en cache: {HasCache}",
                cachedBefore != null);
            var sessionInfo = FrontierChantiersInspectAnalyzer.BuildSessionInfoWhenNoAccessToken(
                sessionDiag, cachedBefore, snap, res.Mode);
            return Ok(FrontierChantiersInspectAnalyzer.BuildNoSession(sessionInfo));
        }

        var modeForInfo = res.Mode;
        const string capEndpoint = "/profile";
        var (status, body) = await _auth.FetchCapiRawAsync(res.Token.AccessToken, capEndpoint, ct);
        if (status == 422)
        {
            _log.LogInformation("[FrontierChantiersInspect] HTTP 422 — refresh token puis nouvel essai CAPI");
            var refreshed = await _auth.RefreshTokenAsync(res.Token.RefreshToken ?? "", ct);
            if (refreshed != null)
            {
                await _oauthSession.PersistAndSetAsync(refreshed, null, ct);
                modeForInfo = FrontierTokenResolutionMode.LiveRefreshed;
                (status, body) = await _auth.FetchCapiRawAsync(refreshed.AccessToken, capEndpoint, ct);
            }
        }

        sessionDiag = _store.GetSessionDiagnostics();
        snap = _oauthSession.GetPersistenceSnapshot();

        _log.LogInformation(
            "[FrontierChantiersInspect] CAPI endpoint={Endpoint} http={Status} bodyLen={Len}",
            capEndpoint, status, body?.Length ?? 0);

        FrontierProfileParseResult? normalized = null;
        string? parseErr = null;
        if (status == 200 && !string.IsNullOrEmpty(body))
        {
            var (fields, err) = _userService.TryParseCapiProfileFields(body);
            normalized = fields;
            parseErr = err;
        }
        else if (status == 200 && string.IsNullOrEmpty(body))
            parseErr = "Réponse CAPI vide";

        var liveSessionInfo = FrontierChantiersInspectAnalyzer.BuildSessionInfoLiveCapiPath(
            sessionDiag, cachedBefore, snap, modeForInfo);
        var response = FrontierChantiersInspectAnalyzer.Build(status, body, normalized, parseErr, capEndpoint, liveSessionInfo);
        return Ok(response);
    }

    /// <summary>
    /// GET /api/integrations/frontier/chantiers-declare-evaluate — /profile (systemName) + /market (station, marketId, ressources chantier) ; pas de JSON brut.
    /// </summary>
    [HttpGet("chantiers-declare-evaluate")]
    public async Task<IActionResult> ChantiersDeclareEvaluate(CancellationToken ct)
    {
        _log.LogInformation("[FrontierChantiersDeclare] Entrée action ChantiersDeclareEvaluate (route enregistrée)");
        var cachedBefore = await _userService.GetCachedProfileAsync(ct);
        var res = await _oauthSession.ResolveEffectiveTokenAsync(ct);
        var snap = _oauthSession.GetPersistenceSnapshot();
        var sessionDiag = _store.GetSessionDiagnostics();

        if (res.Token == null || string.IsNullOrEmpty(res.Token.AccessToken))
        {
            _log.LogInformation("[FrontierChantiersDeclare] Pas de token OAuth résolu");
            var sessionInfo = FrontierChantiersInspectAnalyzer.BuildSessionInfoWhenNoAccessToken(
                sessionDiag, cachedBefore, snap, res.Mode);
            return Ok(new FrontierChantiersDeclareEvaluateResponse(
                Ok: false,
                Error: "oauth",
                CanDeclareChantier: false,
                UserMessage: "Connexion Frontier requise (token OAuth).",
                SystemName: null,
                StationName: null,
                MarketId: null,
                CommanderName: null,
                MarketSummary: null,
                ProfileHttpStatus: 0,
                MarketHttpStatus: 0,
                SessionInfo: sessionInfo));
        }

        var modeForInfo = res.Mode;
        const string capProfile = "/profile";
        const string capMarket = "/market";

        var (profileStatus, profileBody) = await _auth.FetchCapiRawAsync(res.Token.AccessToken, capProfile, ct);
        if (profileStatus == 422)
        {
            _log.LogInformation("[FrontierChantiersDeclare] /profile HTTP 422 — refresh puis nouvel essai");
            var refreshed = await _auth.RefreshTokenAsync(res.Token.RefreshToken ?? "", ct);
            if (refreshed != null)
            {
                await _oauthSession.PersistAndSetAsync(refreshed, null, ct);
                modeForInfo = FrontierTokenResolutionMode.LiveRefreshed;
                res = new FrontierTokenResolutionResult(refreshed, modeForInfo);
                (profileStatus, profileBody) = await _auth.FetchCapiRawAsync(refreshed.AccessToken, capProfile, ct);
            }
        }

        FrontierProfileParseResult? profile = null;
        string? profileParseErr = null;
        if (profileStatus == 200 && !string.IsNullOrEmpty(profileBody))
        {
            var (fields, err) = _userService.TryParseCapiProfileFields(profileBody);
            profile = fields;
            profileParseErr = err;
        }

        var access = res.Token!.AccessToken;
        var (marketStatus, marketBody) = await _auth.FetchCapiRawAsync(access, capMarket, ct);
        if (marketStatus == 422)
        {
            _log.LogInformation("[FrontierChantiersDeclare] /market HTTP 422 — refresh puis nouvel essai");
            var refreshed = await _auth.RefreshTokenAsync(res.Token.RefreshToken ?? "", ct);
            if (refreshed != null)
            {
                await _oauthSession.PersistAndSetAsync(refreshed, null, ct);
                modeForInfo = FrontierTokenResolutionMode.LiveRefreshed;
                access = refreshed.AccessToken;
                (marketStatus, marketBody) = await _auth.FetchCapiRawAsync(access, capMarket, ct);
            }
        }

        FrontierMarketBusinessSummary? marketBiz = null;
        if (marketStatus == 200 && !string.IsNullOrEmpty(marketBody))
            marketBiz = FrontierMarketBusinessParser.TryParse(marketBody, out _);

        sessionDiag = _store.GetSessionDiagnostics();
        snap = _oauthSession.GetPersistenceSnapshot();
        var liveSessionInfo = FrontierChantiersInspectAnalyzer.BuildSessionInfoLiveCapiPath(
            sessionDiag, cachedBefore, snap, modeForInfo);

        static FrontierChantiersDeclareEvaluateResponse Fail(
            string err,
            string msg,
            FrontierProfileParseResult? p,
            FrontierMarketBusinessSummary? m,
            int ps,
            int ms,
            FrontierChantiersInspectSessionInfo si) =>
            new(
                Ok: false,
                Error: err,
                CanDeclareChantier: false,
                UserMessage: msg,
                SystemName: p?.LastSystemName?.Trim(),
                StationName: m?.StationName,
                MarketId: m?.MarketId,
                CommanderName: string.IsNullOrEmpty(p?.CommanderName) ? null : p.CommanderName,
                MarketSummary: m,
                ProfileHttpStatus: ps,
                MarketHttpStatus: ms,
                SessionInfo: si);

        if (profileStatus != 200 || profile == null || !string.IsNullOrEmpty(profileParseErr))
        {
            return Ok(Fail(
                "profile",
                profileStatus != 200
                    ? $"Profil Frontier indisponible (HTTP {profileStatus})."
                    : "Profil Frontier illisible.",
                profile,
                marketBiz,
                profileStatus,
                marketStatus,
                liveSessionInfo));
        }

        if (profile.IsDocked == false)
        {
            return Ok(Fail(
                "not_docked",
                "Vous n’êtes pas docké à une station.",
                profile,
                marketBiz,
                profileStatus,
                marketStatus,
                liveSessionInfo));
        }

        var systemName = profile.LastSystemName?.Trim();
        if (string.IsNullOrEmpty(systemName))
        {
            return Ok(Fail(
                "missing_system",
                "Système courant introuvable dans le profil.",
                profile,
                marketBiz,
                profileStatus,
                marketStatus,
                liveSessionInfo));
        }

        if (marketStatus != 200 || string.IsNullOrEmpty(marketBody))
        {
            return Ok(Fail(
                "market",
                marketStatus != 200
                    ? $"Marché indisponible (HTTP {marketStatus})."
                    : "Réponse /market vide.",
                profile,
                marketBiz,
                profileStatus,
                marketStatus,
                liveSessionInfo));
        }

        if (marketBiz == null)
        {
            return Ok(Fail(
                "market_parse",
                "Impossible d’analyser le marché (CAPI /market).",
                profile,
                null,
                profileStatus,
                marketStatus,
                liveSessionInfo));
        }

        var stName = marketBiz.StationName?.Trim();
        var mId = string.IsNullOrWhiteSpace(marketBiz.MarketId) ? null : marketBiz.MarketId.Trim();

        if (string.IsNullOrEmpty(stName))
        {
            return Ok(Fail(
                "missing_station_name",
                "Nom de station introuvable dans /market (champ name).",
                profile,
                marketBiz,
                profileStatus,
                marketStatus,
                liveSessionInfo));
        }

        if (!marketBiz.HasConstructionResources || marketBiz.ConstructionResourcesCount <= 0)
        {
            return Ok(Fail(
                "no_construction_resources",
                "Aucune ressource de construction listée sur ce marché (chantier non détecté).",
                profile,
                marketBiz,
                profileStatus,
                marketStatus,
                liveSessionInfo));
        }

        _log.LogInformation(
            "[FrontierChantiersDeclare] Éligible — système={System} station={Station} marketId={MarketId} commodities={N}",
            systemName, stName, mId ?? "(absent)", marketBiz.ConstructionResourcesCount);

        return Ok(new FrontierChantiersDeclareEvaluateResponse(
            Ok: true,
            Error: null,
            CanDeclareChantier: true,
            UserMessage: "Vous pouvez ajouter ce chantier.",
            SystemName: systemName,
            StationName: stName,
            MarketId: mId,
            CommanderName: string.IsNullOrEmpty(profile.CommanderName) ? null : profile.CommanderName,
            MarketSummary: marketBiz,
            ProfileHttpStatus: profileStatus,
            MarketHttpStatus: marketStatus,
            SessionInfo: liveSessionInfo));
    }

    /// <summary>GET /api/integrations/frontier/chantiers-declared — chantiers actifs persistés (guilde courante).</summary>
    [HttpGet("chantiers-declared")]
    public async Task<IActionResult> GetDeclaredChantiers(CancellationToken ct)
    {
        var list = await _declaredChantiers.GetActiveForGuildAsync(_currentGuild.CurrentGuildId, ct);
        return Ok(list);
    }

    /// <summary>
    /// GET /api/integrations/frontier/chantiers-declared/me — chantiers actifs du commandant Frontier courant (guilde courante, CmdrName = profil CAPI).
    /// </summary>
    [HttpGet("chantiers-declared/me")]
    public async Task<IActionResult> GetDeclaredChantiersMine(CancellationToken ct)
    {
        var res = await _oauthSession.ResolveEffectiveTokenAsync(ct);
        if (res.Token == null || string.IsNullOrEmpty(res.Token.AccessToken))
            return Unauthorized(new { message = "Connexion Frontier requise pour lister vos chantiers déclarés." });

        var profile = await _userService.GetProfileAsync(ct);
        if (profile == null || string.IsNullOrWhiteSpace(profile.CommanderName))
        {
            _log.LogInformation("[DeclaredChantiers] GET me — profil sans CommanderName, liste vide.");
            return Ok(Array.Empty<DeclaredChantierListItemDto>());
        }

        var list = await _declaredChantiers.GetActiveForGuildForCommanderAsync(
            _currentGuild.CurrentGuildId,
            profile.CommanderName,
            ct);
        return Ok(list);
    }

    /// <summary>
    /// GET /api/integrations/frontier/chantiers-declared/others — chantiers actifs des autres CMDRs (guilde courante, CmdrName ≠ commandant courant).
    /// </summary>
    [HttpGet("chantiers-declared/others")]
    public async Task<IActionResult> GetDeclaredChantiersOthers(CancellationToken ct)
    {
        var res = await _oauthSession.ResolveEffectiveTokenAsync(ct);
        if (res.Token == null || string.IsNullOrEmpty(res.Token.AccessToken))
            return Unauthorized(new { message = "Connexion Frontier requise pour lister les chantiers des autres CMDRs." });

        var profile = await _userService.GetProfileAsync(ct);
        var cmdr = profile?.CommanderName;
        var list = await _declaredChantiers.GetActiveForGuildExcludingCommanderAsync(
            _currentGuild.CurrentGuildId,
            cmdr,
            ct);
        return Ok(list);
    }

    /// <summary>POST /api/integrations/frontier/chantiers-declared — upsert après évaluation réussie (OAuth requis).</summary>
    [HttpPost("chantiers-declared")]
    public async Task<IActionResult> PostDeclaredChantier([FromBody] DeclaredChantierPersistRequest? body, CancellationToken ct)
    {
        var res = await _oauthSession.ResolveEffectiveTokenAsync(ct);
        if (res.Token == null || string.IsNullOrEmpty(res.Token.AccessToken))
            return Unauthorized(new { message = "Connexion Frontier requise pour enregistrer un chantier." });

        if (body == null
            || string.IsNullOrWhiteSpace(body.SystemName)
            || string.IsNullOrWhiteSpace(body.StationName))
            return BadRequest(new { message = "systemName et stationName sont requis." });

        // Aligner CmdrName avec GET /chantiers-declared/me : même source que le filtre (profil CAPI).
        // Si le body n’a pas de CommanderName (ex. évaluation sans nom), Upsert mettait « — » et /me ne retrouvait jamais la ligne.
        var profile = await _userService.GetProfileAsync(ct);
        var commanderForPersist = string.IsNullOrWhiteSpace(body.CommanderName)
            ? profile?.CommanderName
            : body.CommanderName.Trim();
        var bodyForUpsert = body with { CommanderName = commanderForPersist };

        var dto = await _declaredChantiers.UpsertAsync(_currentGuild.CurrentGuildId, bodyForUpsert, ct);
        return Ok(dto);
    }

    /// <summary>
    /// POST /api/integrations/frontier/chantiers-declared/refresh-all — met à jour tous les chantiers actifs (GET /market puis /market?marketId= par ligne), sans exiger d’être docké.
    /// </summary>
    [HttpPost("chantiers-declared/refresh-all")]
    public async Task<IActionResult> PostDeclaredChantiersRefreshAll(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var (access, profile, fail) = await ResolveFrontierAccessAndProfileAsync(ct);
        if (fail != null)
            return fail;

        var guildId = _currentGuild.CurrentGuildId;
        var rows = await _declaredChantiers.GetActiveTrackedRowsAsync(guildId, ct);
        if (rows.Count == 0)
        {
            sw.Stop();
            return Ok(new DeclaredChantierRefreshAllResultDto(0, 0, 0, sw.ElapsedMilliseconds, null));
        }

        var profileSystemKey = profile!.LastSystemName?.Trim();
        var pending = rows.Select(r => r.Id).ToHashSet();
        var totalUpdated = 0;
        var totalDeactivated = 0;

        const string capMarket = "/market";

        var (marketStatus, marketBody) = await _auth.FetchCapiRawAsync(access!, capMarket, ct);
        if (marketStatus == 422)
        {
            var res = await _oauthSession.ResolveEffectiveTokenAsync(ct);
            var refreshed = await _auth.RefreshTokenAsync(res.Token?.RefreshToken ?? "", ct);
            if (refreshed != null)
            {
                await _oauthSession.PersistAndSetAsync(refreshed, null, ct);
                access = refreshed.AccessToken;
                (marketStatus, marketBody) = await _auth.FetchCapiRawAsync(access, capMarket, ct);
            }
        }

        if (marketStatus == 200 && !string.IsNullOrEmpty(marketBody))
        {
            var marketBiz = FrontierMarketBusinessParser.TryParse(marketBody, out _);
            if (marketBiz != null && marketBiz.HasConstructionResources && marketBiz.ConstructionResourcesCount > 0)
            {
                var matches = DeclaredChantiersService.FindRowsMatchingMarketSummary(rows, marketBiz, profileSystemKey);
                if (matches.Count > 0)
                {
                    var (u, d) = await _declaredChantiers.ApplyMarketSummaryToRowsAsync(matches, marketBiz, ct);
                    totalUpdated += u;
                    totalDeactivated += d;
                    foreach (var m in matches)
                        pending.Remove(m.Id);
                }
            }
        }

        foreach (var snapshot in rows)
        {
            if (!pending.Contains(snapshot.Id))
                continue;
            var mid = snapshot.MarketId?.Trim();
            if (string.IsNullOrEmpty(mid))
                continue;

            var row = await _declaredChantiers.GetActiveTrackedByIdAsync(guildId, snapshot.Id, ct);
            if (row == null)
            {
                pending.Remove(snapshot.Id);
                continue;
            }

            var endpoint = $"{capMarket}?marketId={Uri.EscapeDataString(mid)}";
            var (st, body) = await _auth.FetchCapiRawAsync(access!, endpoint, ct);
            if (st != 200 || string.IsNullOrEmpty(body))
                continue;

            var mkt = FrontierMarketBusinessParser.TryParse(body, out _);
            if (mkt == null || !mkt.HasConstructionResources || mkt.ConstructionResourcesCount <= 0)
                continue;

            var one = new List<DeclaredChantier> { row };
            var match = DeclaredChantiersService.FindRowsMatchingMarketSummary(one, mkt, profileSystemKey);
            if (match.Count == 0)
                continue;

            var (u2, d2) = await _declaredChantiers.ApplyMarketSummaryToRowsAsync(match, mkt, ct);
            totalUpdated += u2;
            totalDeactivated += d2;
            pending.Remove(snapshot.Id);
        }

        sw.Stop();
        var skipped = pending.Count;
        _log.LogInformation(
            "[DeclaredChantiersRefreshAll] guild={Guild} rows={Rows} updated={U} deactivated={D} skipped={S} ms={Ms}",
            guildId,
            rows.Count,
            totalUpdated,
            totalDeactivated,
            skipped,
            sw.ElapsedMilliseconds);

        string? note = null;
        if (skipped > 0)
            note =
                "Certains chantiers n’ont pas pu être rafraîchis (pas de marketId, ou CAPI /market indisponible pour cette station).";

        return Ok(new DeclaredChantierRefreshAllResultDto(totalUpdated, totalDeactivated, skipped, sw.ElapsedMilliseconds, note));
    }

    /// <summary>
    /// POST /api/integrations/frontier/chantiers-declared/refresh-one — rafraîchit un chantier actif par id SQL (marketId puis /market courant).
    /// </summary>
    [HttpPost("chantiers-declared/refresh-one")]
    public async Task<IActionResult> PostDeclaredChantiersRefreshOne([FromBody] DeclaredChantierRefreshOneRequest? body, CancellationToken ct)
    {
        if (body == null || body.Id <= 0)
            return BadRequest(new { message = "id de chantier requis." });

        var sw = Stopwatch.StartNew();
        var (access, profile, fail) = await ResolveFrontierAccessAndProfileAsync(ct);
        if (fail != null)
            return fail;

        var guildId = _currentGuild.CurrentGuildId;
        var row = await _declaredChantiers.GetActiveTrackedByIdAsync(guildId, body.Id, ct);
        if (row == null)
            return NotFound(new { message = "Chantier actif introuvable." });

        var profileSystemKey = profile!.LastSystemName?.Trim();
        const string capMarket = "/market";

        FrontierMarketBusinessSummary? applied = null;

        var mid = row.MarketId?.Trim();
        if (!string.IsNullOrEmpty(mid))
        {
            var endpoint = $"{capMarket}?marketId={Uri.EscapeDataString(mid)}";
            var (st, mbody) = await _auth.FetchCapiRawAsync(access!, endpoint, ct);
            if (st == 200 && !string.IsNullOrEmpty(mbody))
            {
                var mkt = FrontierMarketBusinessParser.TryParse(mbody, out _);
                if (mkt != null && mkt.HasConstructionResources && mkt.ConstructionResourcesCount > 0)
                {
                    var match = DeclaredChantiersService.FindRowsMatchingMarketSummary(
                        new List<DeclaredChantier> { row },
                        mkt,
                        profileSystemKey);
                    if (match.Count > 0)
                    {
                        await _declaredChantiers.ApplyMarketSummaryToRowsAsync(match, mkt, ct);
                        applied = mkt;
                    }
                }
            }
        }

        if (applied == null)
        {
            var (marketStatus, marketBody) = await _auth.FetchCapiRawAsync(access!, capMarket, ct);
            if (marketStatus == 422)
            {
                var res = await _oauthSession.ResolveEffectiveTokenAsync(ct);
                var refreshed = await _auth.RefreshTokenAsync(res.Token?.RefreshToken ?? "", ct);
                if (refreshed != null)
                {
                    await _oauthSession.PersistAndSetAsync(refreshed, null, ct);
                    access = refreshed.AccessToken;
                    (marketStatus, marketBody) = await _auth.FetchCapiRawAsync(access, capMarket, ct);
                }
            }

            if (marketStatus != 200 || string.IsNullOrEmpty(marketBody))
            {
                sw.Stop();
                _log.LogWarning("[DeclaredChantiersRefreshOne] id={Id} fail: market HTTP {Status} ms={Ms}", body.Id, marketStatus, sw.ElapsedMilliseconds);
                return BadRequest(new { message = $"Marché indisponible (HTTP {marketStatus})." });
            }

            var marketBiz = FrontierMarketBusinessParser.TryParse(marketBody, out var parseErr);
            if (marketBiz == null)
            {
                sw.Stop();
                return BadRequest(new { message = parseErr ?? "Impossible d’analyser le marché CAPI." });
            }

            if (!marketBiz.HasConstructionResources || marketBiz.ConstructionResourcesCount <= 0)
            {
                sw.Stop();
                return BadRequest(new { message = "Aucune ressource de construction sur ce marché." });
            }

            var rowAgain = await _declaredChantiers.GetActiveTrackedByIdAsync(guildId, body.Id, ct);
            if (rowAgain == null)
            {
                sw.Stop();
                var dtoEnded = await _declaredChantiers.GetListItemDtoByIdAsync(guildId, body.Id, ct);
                return dtoEnded != null ? Ok(dtoEnded) : NotFound(new { message = "Chantier terminé ou introuvable." });
            }

            var matches = DeclaredChantiersService.FindRowsMatchingMarketSummary(
                new List<DeclaredChantier> { rowAgain },
                marketBiz,
                profileSystemKey);
            if (matches.Count == 0)
            {
                sw.Stop();
                return NotFound(new
                {
                    message =
                        "Aucune donnée marché ne correspond à ce chantier (essayez depuis la station ou vérifiez le marketId).",
                });
            }

            await _declaredChantiers.ApplyMarketSummaryToRowsAsync(matches, marketBiz, ct);
            applied = marketBiz;
        }

        sw.Stop();
        var dto = await _declaredChantiers.GetListItemDtoByIdAsync(guildId, body.Id, ct);
        _log.LogInformation(
            "[DeclaredChantiersRefreshOne] id={Id} active={Active} ms={Ms} station={St}",
            body.Id,
            dto?.Active ?? false,
            sw.ElapsedMilliseconds,
            dto?.StationName ?? "—");

        return dto != null ? Ok(dto) : NotFound(new { message = "Chantier introuvable après mise à jour." });
    }

    private async Task<(string? Access, FrontierProfileParseResult? Profile, IActionResult? Fail)> ResolveFrontierAccessAndProfileAsync(
        CancellationToken ct)
    {
        var res = await _oauthSession.ResolveEffectiveTokenAsync(ct);
        if (res.Token == null || string.IsNullOrEmpty(res.Token.AccessToken))
            return (null, null, Unauthorized(new { message = "Connexion Frontier requise pour rafraîchir les chantiers." }));

        var access = res.Token.AccessToken;
        const string capProfile = "/profile";
        var (profileStatus, profileBody) = await _auth.FetchCapiRawAsync(access, capProfile, ct);
        if (profileStatus == 422)
        {
            _log.LogInformation("[DeclaredChantiersRefresh] /profile HTTP 422 — refresh puis nouvel essai");
            var refreshed = await _auth.RefreshTokenAsync(res.Token.RefreshToken ?? "", ct);
            if (refreshed != null)
            {
                await _oauthSession.PersistAndSetAsync(refreshed, null, ct);
                access = refreshed.AccessToken;
                (profileStatus, profileBody) = await _auth.FetchCapiRawAsync(access, capProfile, ct);
            }
        }

        if (profileStatus != 200 || string.IsNullOrEmpty(profileBody))
            return (null, null, BadRequest(new { message = $"Profil Frontier indisponible (HTTP {profileStatus})." }));

        var (fields, err) = _userService.TryParseCapiProfileFields(profileBody);
        if (fields == null || !string.IsNullOrEmpty(err))
            return (null, null, BadRequest(new { message = "Profil Frontier illisible." }));

        return (access, fields, null);
    }

    /// <summary>GET /api/integrations/frontier/debug-url — retourne l'URL d'autorisation complète pour inspection (éviter 406).</summary>
    [HttpGet("debug-url")]
    public IActionResult DebugAuthorizationUrl()
    {
        if (!_auth.IsAuthConfigured)
            return BadRequest(new { error = "Frontier:ClientId et Frontier:RedirectUri requis." });
        var debug = _auth.GetAuthorizationUrlDebug();
        if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return Ok(debug);
        var html = BuildDebugUrlHtml(debug);
        return Content(html, "text/html; charset=utf-8");
    }

    private static string BuildDebugUrlHtml(object debug)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(debug, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var escaped = System.Net.WebUtility.HtmlEncode(json);
        var css = "body{font-family:monospace;margin:1rem;background:#1a1a2e;color:#e6edf3;} pre{background:#2d2d44;padding:1rem;overflow-x:auto;border-radius:6px;} a{color:#6ee7b7;}";
        return $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>Frontier OAuth Debug</title><style>{css}</style></head><body><h1>URL OAuth Frontier (debug)</h1><pre>{escaped}</pre><p><a href='/api/integrations/frontier/start'>→ Lancer OAuth</a></p></body></html>";
    }

    /// <summary>GET /api/integrations/frontier/start — redirection 302 directe vers Frontier login (lance le flux OAuth).</summary>
    [HttpGet("start")]
    public IActionResult StartOAuth()
    {
        if (!_auth.IsAuthConfigured)
            return BadRequest("Frontier:ClientId et Frontier:RedirectUri requis. Configurer appsettings.");
        var (authUrl, state, verifier) = _auth.BuildAuthorizationUrl();
        _store.SetPendingAuth(state, verifier);
        return Redirect(authUrl);
    }

    /// <summary>GET /api/integrations/frontier/test — diagnostic auth + CAPI. HTML si Accept:text/html.</summary>
    [HttpGet("test")]
    public async Task<IActionResult> Test([FromQuery] bool startOAuth = false, CancellationToken ct = default)
    {
        var result = new
        {
            authConfigured = _auth.IsAuthConfigured,
            clientIdConfigured = !string.IsNullOrWhiteSpace(_auth.ClientId),
            redirectUriConfigured = !string.IsNullOrWhiteSpace(_auth.RedirectUri),
            clientSecretConfigured = !string.IsNullOrWhiteSpace(_auth.ClientSecret),
            oauthStartUrl = (string?)null,
            tokenObtained = false,
            capiTest = (object?)null,
            conclusion = "",
        };

        // 1. Config status (mémoire + session persistée + refresh si besoin)
        var effective = await _oauthSession.GetEffectiveTokenAsync(ct);
        var hasToken = effective != null && !string.IsNullOrEmpty(effective.AccessToken);
        object? capiTest = null;

        // 2. Start OAuth flow if requested
        if (startOAuth && _auth.IsAuthConfigured)
        {
            var (authUrl, state, verifier) = _auth.BuildAuthorizationUrl();
            _store.SetPendingAuth(state, verifier);
            var oauthPayload = new
            {
                authConfigured = true,
                clientIdConfigured = true,
                redirectUriConfigured = true,
                clientSecretConfigured = !string.IsNullOrWhiteSpace(_auth.ClientSecret),
                oauthStartUrl = authUrl,
                tokenObtained = hasToken,
                capiTest,
                conclusion = "Cliquer sur le lien ci-dessous pour vous connecter à Frontier.",
            };
            if (!Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return Content(BuildTestHtml(true, hasToken, authUrl, null, null, null, "Cliquer sur le lien ci-dessous pour vous connecter à Frontier."), "text/html; charset=utf-8");
            return Ok(oauthPayload);
        }

        // 3. Token existant : afficher le rapport complet (du store ou en recalculant)
        if (hasToken)
        {
            var token = effective!;
            var report = _store.GetReport();
            if (report == null)
            {
                report = await _auth.RunFullValidationAsync(token, ct);
                await _oauthSession.PersistAndSetAsync(token, report, ct);
            }

            var payload = new
            {
                authConfigured = _auth.IsAuthConfigured,
                clientIdConfigured = !string.IsNullOrWhiteSpace(_auth.ClientId),
                redirectUriConfigured = !string.IsNullOrWhiteSpace(_auth.RedirectUri),
                clientSecretConfigured = !string.IsNullOrWhiteSpace(_auth.ClientSecret),
                oauthStartUrl = (string?)null,
                tokenObtained = true,
                report = new
                {
                    oAuthOk = report.OAuthOk,
                    tokenOk = report.TokenOk,
                    accessTokenObtained = report.AccessTokenObtained,
                    refreshTokenObtained = report.RefreshTokenObtained,
                    tokenExpiresIn = report.TokenExpiresIn,
                    scope = report.Scope,
                    meOk = report.MeOk,
                    me = new { report.MeResult.HttpStatusCode, report.MeResult.ResponseLength, report.MeResult.DetectedFields, preview = Truncate(report.MeResult.ResponsePreview, 300) },
                    decodeOk = report.DecodeOk,
                    decode = new { report.DecodeResult.HttpStatusCode, report.DecodeResult.ResponseLength, report.DecodeResult.DetectedFields, preview = Truncate(report.DecodeResult.ResponsePreview, 300) },
                    capiOk = report.CapiOk,
                    capi = new { report.CapiResult.HttpStatusCode, report.CapiResult.ResponseLength, report.CapiResult.DetectedFields, preview = Truncate(report.CapiResult.ResponsePreview, 300) },
                    conclusion = report.Conclusion,
                    dataAvailable = report.CapiOk ? report.CapiResult.DetectedFields : null,
                },
            };
            if (!Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return Content(BuildTestReportHtml(report), "text/html; charset=utf-8");
            return Ok(payload);
        }

        // 4. No token, just config
        var msg = !_auth.IsAuthConfigured
            ? "Configurer Frontier:ClientId et Frontier:RedirectUri dans appsettings. Puis ?startOAuth=true pour lancer le flux."
            : "Pas de token. Appeler ?startOAuth=true pour obtenir l'URL OAuth, terminer le flux via /callback.";
        var configPayload = new
        {
            result.authConfigured,
            result.clientIdConfigured,
            result.redirectUriConfigured,
            result.clientSecretConfigured,
            result.oauthStartUrl,
            result.tokenObtained,
            result.capiTest,
            conclusion = msg,
        };
        if (!Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return Content(BuildTestHtml(result.authConfigured, result.tokenObtained, null, null, null, null, msg), "text/html; charset=utf-8");
        return Ok(configPayload);
    }

    private static string BuildTestReportHtml(FrontierValidationReport r)
    {
        var css = "body{font-family:system-ui;margin:0;padding:1rem;background:#1a1a2e;color:#e6edf3;} .card{background:#2d2d44;padding:1rem;border-radius:8px;margin-bottom:1rem;} h1,h2{color:#6ee7b7;} .ok{color:#6ee7b7;} .err{color:#f87171;} pre{font-size:0.85em;overflow-x:auto;} a{color:#93c5fd;}";
        var row = (string lab, bool ok, int status, int len, List<string>? fields) =>
            $"<tr><td>{lab}</td><td class='{(ok ? "ok" : "err")}'>{status}</td><td>{len} bytes</td><td>{string.Join(", ", fields ?? new List<string>())}</td></tr>";
        var table = "<table style='border-collapse:collapse;'><tr><th>Étape</th><th>HTTP</th><th>Taille</th><th>Champs</th></tr>" +
            $"<tr><td>Token</td><td class='{(r.TokenOk ? "ok" : "err")}'>—</td><td>access+refresh</td><td>expires_in={r.TokenExpiresIn}s</td></tr>" +
            row("/me", r.MeOk, r.MeResult.HttpStatusCode, r.MeResult.ResponseLength, r.MeResult.DetectedFields) +
            row("/decode", r.DecodeOk, r.DecodeResult.HttpStatusCode, r.DecodeResult.ResponseLength, r.DecodeResult.DetectedFields) +
            row("CAPI /profile", r.CapiOk, r.CapiResult.HttpStatusCode, r.CapiResult.ResponseLength, r.CapiResult.DetectedFields) +
            "</table>";
        var previews = $"<details><summary>/me</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.MeResult.ResponsePreview, 500))}</pre></details>" +
            $"<details><summary>/decode</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.DecodeResult.ResponsePreview, 500))}</pre></details>" +
            $"<details><summary>CAPI</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.CapiResult.ResponsePreview, 500))}</pre></details>";
        return $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>Frontier Test</title><style>{css}</style></head><body>" +
            "<div class='card'><h1>Frontier OAuth2 / CAPI</h1><p><a href='/api/integrations/frontier/start'>Démarrer OAuth</a> | <a href='/api/integrations/frontier/test'>Rafraîchir</a></p></div>" +
            "<div class='card'><h2>Compte rendu</h2><p><strong>" + System.Net.WebUtility.HtmlEncode(r.Conclusion) + "</strong></p></div>" +
            "<div class='card'><h2>Détail</h2>" + table + "</div>" +
            "<div class='card'><h2>Aperçus JSON</h2>" + previews + "</div></body></html>";
    }

    private static string BuildTestHtml(bool authConfigured, bool tokenObtained, string? oauthStartUrl, int? capiStatus, int? capiLen, IReadOnlyList<string>? capiFields, string conclusion)
    {
        var css = "body{font-family:system-ui;margin:0;padding:1rem;background:#1a1a2e;color:#e6edf3;} .card{background:#2d2d44;padding:1rem;border-radius:8px;margin-bottom:1rem;} h1{color:#6ee7b7;} .ok{color:#6ee7b7;} .err{color:#f87171;} a{color:#93c5fd;}";
        var auth = authConfigured ? "<span class='ok'>Oui</span>" : "<span class='err'>Non</span>";
        var token = tokenObtained ? "<span class='ok'>Oui</span>" : "<span class='err'>Non</span>";
        var oauthLink = !string.IsNullOrEmpty(oauthStartUrl)
            ? $"<p><strong>Option A (redirection directe):</strong> <a href='/api/integrations/frontier/start'>Démarrer OAuth (redirige vers Frontier)</a></p><p><strong>Option B (nouvel onglet):</strong> <a href='{System.Net.WebUtility.HtmlEncode(oauthStartUrl)}' target='_blank'>Ouvrir dans un nouvel onglet</a></p>"
            : "";
        var capiSection = capiStatus.HasValue
            ? $"<div class='card'><h2>CAPI /profile</h2><p>HTTP {capiStatus.Value} | {capiLen ?? 0} bytes</p><p>Champs: {string.Join(", ", capiFields ?? Array.Empty<string>())}</p></div>"
            : "";
        return $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>Frontier Diagnostic</title><style>{css}</style></head><body>" +
            "<div class='card'><h1>Frontier OAuth2 / CAPI</h1><p>Auth configuré: " + auth + " | Token obtenu: " + token + "</p>" + oauthLink +
            "<p><a href='/api/integrations/frontier/start'>Démarrer OAuth</a> (redirige vers Frontier) | <a href='/api/integrations/frontier/test'>Rafraîchir</a></p></div>" +
            capiSection +
            "<div class='card'><p>" + System.Net.WebUtility.HtmlEncode(conclusion) + "</p></div></body></html>";
    }

    /// <summary>GET /api/integrations/frontier/callback — callback OAuth après auth utilisateur.</summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return Ok(new { success = false, error, message = "L'utilisateur a refusé ou une erreur OAuth s'est produite." });
        }

        if (string.IsNullOrEmpty(code))
        {
            return BadRequest(new { success = false, error = "code manquant" });
        }

        var consumed = _store.ConsumePendingAuth(state ?? "");
        if (consumed == null || string.IsNullOrEmpty(consumed.Value.Verifier))
        {
            var msg = "Tentative OAuth expirée ou remplacée. Relancez la connexion Frontier.";
            var frontendBase = _auth.FrontendBaseUrl;
            if (!string.IsNullOrWhiteSpace(frontendBase))
            {
                var redirectUri = frontendBase.TrimEnd('/') + "/?frontier=error&message=" + Uri.EscapeDataString(msg);
                return Redirect(redirectUri);
            }
            return BadRequest(new { success = false, error = "state_invalid", message = msg });
        }

        var verifier = consumed.Value.Verifier!;

        var token = await _auth.ExchangeCodeAsync(code, verifier, ct);
        if (token == null)
        {
            return Ok(new { success = false, error = "Échec échange code/token. Vérifier les logs." });
        }

        var report = await _auth.RunFullValidationAsync(token, ct);
        await _oauthSession.PersistAndSetAsync(token, report, ct);
        _journalUnifiedSync.ClearAuthErrorStateAfterFrontierLogin();

        _ = await _userService.GetProfileAsync(ct);

        var baseUrl = _auth.FrontendBaseUrl;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return Redirect(baseUrl.TrimEnd('/') + "/?frontier=success");
        }

        if (!Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return Content(BuildCallbackReportHtml(report), "text/html; charset=utf-8");
        return Ok(BuildCallbackReportPayload(report));
    }

    private static object BuildCallbackReportPayload(FrontierValidationReport r)
    {
        return new
        {
            success = true,
            report = new
            {
                oAuthOk = r.OAuthOk,
                tokenOk = r.TokenOk,
                accessTokenObtained = r.AccessTokenObtained,
                refreshTokenObtained = r.RefreshTokenObtained,
                tokenExpiresIn = r.TokenExpiresIn,
                scope = r.Scope,
                meOk = r.MeOk,
                me = new { r.MeResult.HttpStatusCode, r.MeResult.ResponseLength, r.MeResult.DetectedFields, preview = Truncate(r.MeResult.ResponsePreview, 300) },
                decodeOk = r.DecodeOk,
                decode = new { r.DecodeResult.HttpStatusCode, r.DecodeResult.ResponseLength, r.DecodeResult.DetectedFields, preview = Truncate(r.DecodeResult.ResponsePreview, 300) },
                capiOk = r.CapiOk,
                capi = new { r.CapiResult.HttpStatusCode, r.CapiResult.ResponseLength, r.CapiResult.DetectedFields, preview = Truncate(r.CapiResult.ResponsePreview, 300) },
                conclusion = r.Conclusion,
                dataAvailable = r.CapiOk ? r.CapiResult.DetectedFields : null,
            },
        };
    }

    private static string BuildCallbackReportHtml(FrontierValidationReport r)
    {
        var css = "body{font-family:system-ui;margin:0;padding:1rem;background:#1a1a2e;color:#e6edf3;} .card{background:#2d2d44;padding:1rem;border-radius:8px;margin-bottom:1rem;} h1,h2{color:#6ee7b7;} .ok{color:#6ee7b7;} .err{color:#f87171;} pre{font-size:0.85em;overflow-x:auto;} a{color:#93c5fd;}";
        var row = (string lab, bool ok, int status, int len, List<string>? fields) =>
            $"<tr><td>{lab}</td><td class='{(ok ? "ok" : "err")}'>{status}</td><td>{len} bytes</td><td>{string.Join(", ", fields ?? new List<string>())}</td></tr>";
        var table = "<table style='border-collapse:collapse;'><tr><th>Étape</th><th>HTTP</th><th>Taille</th><th>Champs</th></tr>" +
            $"<tr><td>Token</td><td class='{(r.TokenOk ? "ok" : "err")}'>—</td><td>access+refresh reçus</td><td>expires_in={r.TokenExpiresIn}s</td></tr>" +
            row("/me", r.MeOk, r.MeResult.HttpStatusCode, r.MeResult.ResponseLength, r.MeResult.DetectedFields) +
            row("/decode", r.DecodeOk, r.DecodeResult.HttpStatusCode, r.DecodeResult.ResponseLength, r.DecodeResult.DetectedFields) +
            row("CAPI /profile", r.CapiOk, r.CapiResult.HttpStatusCode, r.CapiResult.ResponseLength, r.CapiResult.DetectedFields) +
            "</table>";
        var previews = $"<details><summary>/me</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.MeResult.ResponsePreview, 500))}</pre></details>" +
            $"<details><summary>/decode</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.DecodeResult.ResponsePreview, 500))}</pre></details>" +
            $"<details><summary>CAPI</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.CapiResult.ResponsePreview, 500))}</pre></details>";
        return $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>Frontier Validation</title><style>{css}</style></head><body>" +
            "<div class='card'><h1>Compte rendu Frontier</h1><p><strong>" + System.Net.WebUtility.HtmlEncode(r.Conclusion) + "</strong></p></div>" +
            "<div class='card'><h2>Résumé</h2>" + table + "</div>" +
            "<div class='card'><h2>Aperçus JSON</h2>" + previews + "</div>" +
            "<div class='card'><p><a href='/api/integrations/frontier/test'>Voir test complet</a></p></div></body></html>";
    }

    private static string Truncate(string? s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "...";
}
