namespace GuildDashboard.Server.DTOs;

/// <summary>
/// Sans secrets : explique l’écart entre « dashboard affiche du Frontier » et « token OAuth disponible pour le CAPI ».
/// Le cache runtime (FrontierTokenStore) complète la ligne SQL ; les jetons sont chiffrés en base (Data Protection).
/// </summary>
public record FrontierChantiersInspectSessionInfo(
    bool OAuthTokenInProcessMemory,
    bool HasAccessToken,
    bool HasRefreshToken,
    bool AccessTokenProbablyExpiredLocalEstimate,
    bool SqlCachedFrontierProfileRowExists,
    DateTime? SqlCachedProfileLastFetchedUtc,
    string HowDashboardGetsFrontierDataSummary,
    string ChantiersInspectBlockedReason,
    bool AppUsesPerUserCookieAuth,
    string ArchitectureNote,
    bool PersistedOAuthSessionRowExists,
    DateTime? PersistedSessionUpdatedUtc,
    string TokenResolutionMode,
    string PersistenceSummaryNote);

/// <summary>Résultat du parse CAPI /profile (champs déjà normalisés côté serveur).</summary>
public record FrontierProfileParseResult(
    string FrontierCustomerId,
    string CommanderName,
    string? SquadronName,
    string? LastSystemName,
    string? ShipName,
    bool? IsDocked,
    string? StationName);

/// <summary>
/// Feuille ou nœud utile pour le diagnostic dock/station — jamais le JSON complet.
/// </summary>
public record FrontierJsonPathCandidate(string Path, string ValueType, string ValuePreview);

/// <summary>Réponse GET chantiers-inspect : données Frontier déjà disponibles + diagnostic chantier/station.</summary>
public record FrontierChantiersInspectResponse(
    bool Ok,
    string? Error,
    DateTime FetchedAtUtc,
    string CapEndpoint,
    int HttpStatus,
    int ApproxProfileJsonChars,
    int RootKeyCount,
    FrontierProfileParseResult? NormalizedFromProfile,
    string? ParseError,
    IReadOnlyList<string> RootKeys,
    IReadOnlyList<string> PropertyPathsSample,
    IReadOnlyList<string> KeywordHits,
    /// <summary>Jusqu’à 20 chemins dont le libellé contient dock/station/starport/location/port (diagnostic).</summary>
    IReadOnlyList<FrontierJsonPathCandidate> DockStationPathCandidates,
    FrontierChantiersDiagnostic Diagnostic,
    string? RawJsonFormattedTruncated,
    FrontierChantiersInspectSessionInfo SessionInfo);

public record FrontierChantiersDiagnostic(
    IReadOnlyList<string> EndpointsInspected,
    IReadOnlyList<string> UsefulFieldsFound,
    IReadOnlyList<string> FieldsMissingForConstructionTracking,
    string Note);
