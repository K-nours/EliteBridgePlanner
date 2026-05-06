namespace GuildDashboard.Server.DTOs;

/// <summary>
/// GET chantiers-declare-evaluate — combine /profile + /market pour déclarer un chantier (pas de JSON brut).
/// </summary>
public record FrontierChantiersDeclareEvaluateResponse(
    bool Ok,
    string? Error,
    bool CanDeclareChantier,
    /// <summary>Message court pour l’UI (français).</summary>
    string UserMessage,
    string? SystemName,
    string? StationName,
    string? MarketId,
    string? CommanderName,
    FrontierMarketBusinessSummary? MarketSummary,
    int ProfileHttpStatus,
    int MarketHttpStatus,
    FrontierChantiersInspectSessionInfo SessionInfo);
