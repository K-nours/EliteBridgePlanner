using System.Text;
using System.Text.Json;
using GuildDashboard.Server.DTOs;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Analyse le JSON CAPI /profile pour le debug « chantiers » : chemins, mots-clés station/market/construction.
/// Ne renvoie pas le JSON brut au client (résumés et listes plafonnés).
/// </summary>
public static class FrontierChantiersInspectAnalyzer
{
    private static readonly string[] Keywords =
    [
        "market", "station", "starport", "dock", "landing", "commodit", "cargo", "construction",
        "colon", "build", "progress", "required", "import", "export", "demand", "supply",
        "blueprint", "engineer", "carrier", "megaship", "settlement", "orbital", "shipyard",
        "outfitting", "commodity", "material", "mission", "faction"
    ];

    private const int MaxPaths = 40;
    private const int MaxRootKeysReturned = 24;
    private const int MaxKeywordHitsReturned = 20;
    private const int MaxUsefulFieldsReturned = 20;
    private const int MaxMissingFieldsReturned = 12;
    private const int MaxNoteChars = 400;
    private const int MaxBodyCharsForAnalysis = 1_500_000;
    private const int MaxArrayElementsWalked = 4;
    private const int MaxDockStationCandidates = 20;
    private const int MaxCandidateValuePreview = 80;
    private const int MaxCandidateWalkDepth = 18;

    /// <summary>
    /// Diagnostic sans secret : pourquoi le dashboard peut afficher du Frontier alors que l’inspection CAPI brute est bloquée.
    /// </summary>
    public static FrontierChantiersInspectSessionInfo BuildSessionInfoWhenNoAccessToken(
        FrontierSessionDiagnostics diag,
        FrontierProfileDto? cached,
        FrontierOAuthPersistenceSnapshot snap,
        FrontierTokenResolutionMode mode)
    {
        var hasCache = cached != null;
        var blocked =
            "Pas d’access token OAuth utilisable pour le CAPI (mémoire + base + refresh). ";
        if (hasCache)
            blocked +=
                "Le dashboard peut afficher CMDR / système / vaisseau depuis FrontierProfiles (cache SQL).";
        else blocked += "Pas de profil Frontier en cache SQL.";

        var how = hasCache
            ? "GetProfileAsync() peut retomber sur GetCachedProfileAsync() (FrontierProfiles) — comme /api/user/me."
            : "Sans profil SQL ni session OAuth résolue, pas de données Frontier affichables côté API.";

        return new FrontierChantiersInspectSessionInfo(
            OAuthTokenInProcessMemory: diag.HasStoredToken,
            HasAccessToken: diag.HasAccessToken,
            HasRefreshToken: diag.HasRefreshToken,
            AccessTokenProbablyExpiredLocalEstimate: diag.AccessProbablyExpired,
            SqlCachedFrontierProfileRowExists: hasCache,
            SqlCachedProfileLastFetchedUtc: cached?.LastFetchedAt,
            HowDashboardGetsFrontierDataSummary: how,
            ChantiersInspectBlockedReason: blocked,
            AppUsesPerUserCookieAuth: false,
            ArchitectureNote:
                "Runtime : FrontierTokenStore. Persistant : table FrontierOAuthSessions (jetons chiffrés via Data Protection).",
            PersistedOAuthSessionRowExists: snap.PersistedRowExists,
            PersistedSessionUpdatedUtc: snap.LastUpdatedUtc,
            TokenResolutionMode: mode == FrontierTokenResolutionMode.ReconnectRequired && hasCache
                ? "cache_only"
                : FormatResolutionMode(mode),
            PersistenceSummaryNote: snap.PersistedRowExists
                ? "Ligne OAuth persistée présente — refresh impossible ou secret Frontier manquant côté serveur."
                : "Aucune session OAuth en base — connectez Frontier pour créer une session persistée.");
    }

    public static FrontierChantiersInspectSessionInfo BuildSessionInfoLiveCapiPath(
        FrontierSessionDiagnostics diag,
        FrontierProfileDto? cachedBeforeCall,
        FrontierOAuthPersistenceSnapshot snap,
        FrontierTokenResolutionMode mode)
    {
        var hasCache = cachedBeforeCall != null;
        return new FrontierChantiersInspectSessionInfo(
            OAuthTokenInProcessMemory: true,
            HasAccessToken: diag.HasAccessToken,
            HasRefreshToken: diag.HasRefreshToken,
            AccessTokenProbablyExpiredLocalEstimate: diag.AccessProbablyExpired,
            SqlCachedFrontierProfileRowExists: hasCache,
            SqlCachedProfileLastFetchedUtc: cachedBeforeCall?.LastFetchedAt,
            HowDashboardGetsFrontierDataSummary:
                "CAPI /profile live puis persistance FrontierProfiles — aligné sur FrontierUserService.",
            ChantiersInspectBlockedReason: "",
            AppUsesPerUserCookieAuth: false,
            ArchitectureNote: "Token résolu pour cet appel (voir TokenResolutionMode).",
            PersistedOAuthSessionRowExists: snap.PersistedRowExists,
            PersistedSessionUpdatedUtc: snap.LastUpdatedUtc,
            TokenResolutionMode: FormatResolutionMode(mode),
            PersistenceSummaryNote: "Session OAuth synchronisée en base après login / refresh.");
    }

    private static string FormatResolutionMode(FrontierTokenResolutionMode mode) => mode switch
    {
        FrontierTokenResolutionMode.LiveMemory => "live_memory",
        FrontierTokenResolutionMode.LiveRestoredFromDatabase => "live_restored",
        FrontierTokenResolutionMode.LiveRefreshed => "live_refreshed",
        FrontierTokenResolutionMode.ReconnectRequired => "reconnect_required",
        _ => "unknown",
    };

    public static FrontierChantiersInspectResponse BuildNoSession(FrontierChantiersInspectSessionInfo sessionInfo)
    {
        return new FrontierChantiersInspectResponse(
            Ok: false,
            Error: "Pas d’access token OAuth utilisable pour le CAPI (voir sessionInfo).",
            FetchedAtUtc: DateTime.UtcNow,
            CapEndpoint: "/profile",
            HttpStatus: 0,
            ApproxProfileJsonChars: 0,
            RootKeyCount: 0,
            NormalizedFromProfile: null,
            ParseError: null,
            RootKeys: Array.Empty<string>(),
            PropertyPathsSample: Array.Empty<string>(),
            KeywordHits: Array.Empty<string>(),
            DockStationPathCandidates: Array.Empty<FrontierJsonPathCandidate>(),
            Diagnostic: new FrontierChantiersDiagnostic(
                EndpointsInspected: new[] { "/profile (non appelé — pas d’access token OAuth résolu : mémoire / base / refresh)" },
                UsefulFieldsFound: Array.Empty<string>(),
                FieldsMissingForConstructionTracking: new[]
                {
                    "Inspection chantier : nécessite un appel CAPI brut — impossible sans access token (voir sessionInfo)."
                },
                Note:
                    "Le dashboard peut montrer un CMDR « connecté » via le cache SQL sans token OAuth actif. Détails : sessionInfo."),
            RawJsonFormattedTruncated: null,
            SessionInfo: sessionInfo);
    }

    public static FrontierChantiersInspectResponse Build(
        int httpStatus,
        string? body,
        FrontierProfileParseResult? normalized,
        string? parseError,
        string capEndpoint,
        FrontierChantiersInspectSessionInfo sessionInfo)
    {
        var fetchedAt = DateTime.UtcNow;
        var approxLen = body?.Length ?? 0;

        if (approxLen > MaxBodyCharsForAnalysis)
        {
            var oversizedNote = $"Réponse CAPI trop volumineuse ({approxLen} car.) — analyse détaillée désactivée (plafond sécurité).";
            return new FrontierChantiersInspectResponse(
                Ok: false,
                Error: "Réponse CAPI trop volumineuse pour analyse.",
                FetchedAtUtc: fetchedAt,
                CapEndpoint: capEndpoint,
                HttpStatus: httpStatus,
                ApproxProfileJsonChars: approxLen,
                RootKeyCount: 0,
                NormalizedFromProfile: normalized,
                ParseError: parseError,
                RootKeys: Array.Empty<string>(),
                PropertyPathsSample: Array.Empty<string>(),
                KeywordHits: Array.Empty<string>(),
                DockStationPathCandidates: Array.Empty<FrontierJsonPathCandidate>(),
                Diagnostic: new FrontierChantiersDiagnostic(
                    EndpointsInspected: new[] { capEndpoint },
                    UsefulFieldsFound: Array.Empty<string>(),
                    FieldsMissingForConstructionTracking: new[] { "Données profil / chantier — réponse brute trop grande." },
                    Note: oversizedNote.Length > MaxNoteChars ? oversizedNote[..MaxNoteChars] + "…" : oversizedNote),
                RawJsonFormattedTruncated: null,
                SessionInfo: sessionInfo);
        }

        var rootKeys = new List<string>();
        var paths = new List<string>();
        var keywordHits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dockStationCandidates = new List<FrontierJsonPathCandidate>();

        var rootKeyCount = 0;
        if (httpStatus == 200 && !string.IsNullOrEmpty(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        rootKeyCount++;
                        if (rootKeys.Count < MaxRootKeysReturned)
                            rootKeys.Add(p.Name);
                    }
                }
                CollectPathsAndKeywords(doc.RootElement, "", 0, paths, keywordHits);
                CollectDockStationPathCandidates(doc.RootElement, "", 0, dockStationCandidates);
            }
            catch (Exception ex)
            {
                parseError ??= ex.Message;
            }
        }

        var pathsSample = paths.Count > MaxPaths ? paths.Take(MaxPaths).ToList() : paths;

        var useful = new List<string>();
        if (normalized != null)
        {
            if (!string.IsNullOrEmpty(normalized.CommanderName)) useful.Add($"commander: {normalized.CommanderName}");
            if (!string.IsNullOrEmpty(normalized.SquadronName)) useful.Add($"squadron: {normalized.SquadronName}");
            if (!string.IsNullOrEmpty(normalized.LastSystemName)) useful.Add($"lastSystem: {normalized.LastSystemName}");
            if (!string.IsNullOrEmpty(normalized.ShipName)) useful.Add($"ship: {normalized.ShipName}");
            if (normalized.IsDocked == true) useful.Add("docked: true");
            else if (normalized.IsDocked == false) useful.Add("docked: false");
            if (!string.IsNullOrEmpty(normalized.StationName)) useful.Add($"station: {normalized.StationName}");
        }
        foreach (var hit in keywordHits.OrderBy(x => x))
        {
            if (useful.Count >= MaxUsefulFieldsReturned)
                break;
            useful.Add($"keyword:{hit}");
        }

        var missing = BuildMissingList(normalized, keywordHits);
        var missingCapped = missing.Count > MaxMissingFieldsReturned
            ? missing.Take(MaxMissingFieldsReturned).ToList()
            : missing;

        var note = new StringBuilder();
        note.Append("CAPI: un GET /profile (identique dashboard). Parse: CMDR, squadron, lastSystem, ship. ");
        note.Append("JSON brut non renvoyé au navigateur (mode sécurité). ");
        if (keywordHits.Count == 0)
            note.Append("Aucun mot-clé chantier/station détecté dans les noms de propriétés.");
        else
            note.Append("Mots-clés correspondants — voir liste tronquée.");

        var noteStr = note.ToString();
        if (noteStr.Length > MaxNoteChars)
            noteStr = noteStr[..MaxNoteChars] + "…";

        var keywordList = keywordHits.OrderBy(x => x).Take(MaxKeywordHitsReturned).ToList();

        var diagnostic = new FrontierChantiersDiagnostic(
            EndpointsInspected: new[] { capEndpoint },
            UsefulFieldsFound: useful,
            FieldsMissingForConstructionTracking: missingCapped,
            Note: noteStr);

        var ok = httpStatus == 200 && normalized != null && string.IsNullOrEmpty(parseError);
        var errMsg = httpStatus != 200
            ? (httpStatus == 0 ? "Échec réseau CAPI" : $"CAPI HTTP {httpStatus}")
            : parseError;

        return new FrontierChantiersInspectResponse(
            Ok: ok,
            Error: errMsg,
            FetchedAtUtc: fetchedAt,
            CapEndpoint: capEndpoint,
            HttpStatus: httpStatus,
            ApproxProfileJsonChars: approxLen,
            RootKeyCount: rootKeyCount,
            NormalizedFromProfile: normalized,
            ParseError: parseError,
            RootKeys: rootKeys,
            PropertyPathsSample: pathsSample,
            KeywordHits: keywordList,
            DockStationPathCandidates: dockStationCandidates,
            Diagnostic: diagnostic,
            RawJsonFormattedTruncated: null,
            SessionInfo: sessionInfo);
    }

    /// <summary>
    /// Chemins JSON dont un segment contient dock / station / starport / location / le segment « port » — max 20, valeurs tronquées.
    /// </summary>
    private static void CollectDockStationPathCandidates(
        JsonElement el,
        string path,
        int depth,
        List<FrontierJsonPathCandidate> candidates)
    {
        if (candidates.Count >= MaxDockStationCandidates || depth > MaxCandidateWalkDepth)
            return;

        if (!string.IsNullOrEmpty(path) && PathMatchesDockStationKeywords(path))
        {
            candidates.Add(CreatePathCandidate(path, el));
            if (candidates.Count >= MaxDockStationCandidates)
                return;
        }

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    var full = string.IsNullOrEmpty(path) ? p.Name : $"{path}.{p.Name}";
                    CollectDockStationPathCandidates(p.Value, full, depth + 1, candidates);
                    if (candidates.Count >= MaxDockStationCandidates)
                        return;
                }
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in el.EnumerateArray())
                {
                    if (i >= MaxArrayElementsWalked)
                        break;
                    var idxPath = $"{path}[{i}]";
                    CollectDockStationPathCandidates(item, idxPath, depth + 1, candidates);
                    if (candidates.Count >= MaxDockStationCandidates)
                        return;
                    i++;
                }
                break;
        }
    }

    /// <summary>True si un segment du chemin (ex. commander.lastStarport.name → lastStarport, name) est pertinent.</summary>
    private static bool PathMatchesDockStationKeywords(string fullPath)
    {
        foreach (var segment in fullPath.Split('.'))
        {
            if (SegmentMatchesDockStationKeywords(segment))
                return true;
        }

        return false;
    }

    private static bool SegmentMatchesDockStationKeywords(string segment)
    {
        var s = StripArrayIndex(segment).ToLowerInvariant();
        if (s.Length == 0) return false;
        if (s.Contains("dock", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Contains("station", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Contains("starport", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Contains("location", StringComparison.OrdinalIgnoreCase)) return true;
        return s.Equals("port", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripArrayIndex(string segment)
    {
        var i = segment.IndexOf('[');
        return i < 0 ? segment : segment[..i];
    }

    private static FrontierJsonPathCandidate CreatePathCandidate(string path, JsonElement el)
    {
        var type = el.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True => "boolean",
            JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            _ => "unknown",
        };

        var preview = el.ValueKind switch
        {
            JsonValueKind.String => TruncatePreview(SanitizeOneLine(el.GetString() ?? "")),
            JsonValueKind.Number => TruncatePreview(el.GetRawText()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            JsonValueKind.Object => ObjectPreviewCompact(el),
            JsonValueKind.Array => $"array[{el.GetArrayLength()}]",
            _ => "",
        };

        return new FrontierJsonPathCandidate(path, type, preview);
    }

    private static string ObjectPreviewCompact(JsonElement el)
    {
        var n = 0;
        foreach (var _ in el.EnumerateObject())
        {
            n++;
            if (n > 48) return "{ … }";
        }

        return n == 0 ? "{}" : $"{{ {n} clés }}";
    }

    private static string SanitizeOneLine(string s)
        => s.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static string TruncatePreview(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Trim();
        return s.Length <= MaxCandidateValuePreview ? s : s[..MaxCandidateValuePreview] + "…";
    }

    private static IReadOnlyList<string> BuildMissingList(FrontierProfileParseResult? normalized, HashSet<string> keywordHits)
    {
        var hits = string.Join(" ", keywordHits);
        var missing = new List<string>();

        if (!hits.Contains("market", StringComparison.OrdinalIgnoreCase))
            missing.Add("marketId / données marché (aucune propriété « market » détectée dans /profile)");
        if (string.IsNullOrEmpty(normalized?.StationName) &&
            !hits.Contains("dock", StringComparison.OrdinalIgnoreCase) && !hits.Contains("station", StringComparison.OrdinalIgnoreCase) && !hits.Contains("starport", StringComparison.OrdinalIgnoreCase))
            missing.Add("station / starport / lastStation (nom d’installation — non résolu dans le parse normalisé)");
        if (!hits.Contains("commodit", StringComparison.OrdinalIgnoreCase) && !hits.Contains("import", StringComparison.OrdinalIgnoreCase) && !hits.Contains("export", StringComparison.OrdinalIgnoreCase))
            missing.Add("commodities / imports / exports / demand (liste marché)");
        if (!hits.Contains("construction", StringComparison.OrdinalIgnoreCase) && !hits.Contains("colon", StringComparison.OrdinalIgnoreCase) && !hits.Contains("build", StringComparison.OrdinalIgnoreCase))
            missing.Add("construction / colonisation / chantier (progress, required commodities)");
        if (string.IsNullOrEmpty(normalized?.LastSystemName))
            missing.Add("système courant (lastSystem) — absent du parse");

        return missing;
    }

    private static void CollectPathsAndKeywords(
        JsonElement el,
        string path,
        int depth,
        List<string> paths,
        HashSet<string> keywordHits)
    {
        if (paths.Count >= MaxPaths || depth > 14)
            return;

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    var name = p.Name;
                    var full = string.IsNullOrEmpty(path) ? name : $"{path}.{name}";
                    if (paths.Count < MaxPaths)
                        paths.Add(full);
                    foreach (var kw in Keywords)
                    {
                        if (name.Contains(kw, StringComparison.OrdinalIgnoreCase))
                            keywordHits.Add($"{full} (~{kw})");
                    }
                    CollectPathsAndKeywords(p.Value, full, depth + 1, paths, keywordHits);
                }
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in el.EnumerateArray())
                {
                    if (i >= MaxArrayElementsWalked)
                        break;
                    var idxPath = $"{path}[{i}]";
                    CollectPathsAndKeywords(item, idxPath, depth + 1, paths, keywordHits);
                    i++;
                }
                break;
        }
    }
}
