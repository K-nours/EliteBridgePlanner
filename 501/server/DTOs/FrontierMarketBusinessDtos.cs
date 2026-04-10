namespace GuildDashboard.Server.DTOs;

/// <summary>Une commodité de construction (requiredConstructionResources.commodities).</summary>
public record FrontierConstructionResourceItem(
    string Name,
    long Required,
    long Provided,
    long Remaining);

/// <summary>Résumé métier compact CAPI /market (pas de JSON brut).</summary>
/// <param name="RequiredConstructionBlockPresent">True si la clé <c>requiredConstructionResources</c> est présente (chantier terminé = commodities vides possibles).</param>
public record FrontierMarketBusinessSummary(
    string? StationName,
    string? MarketId,
    bool HasConstructionResources,
    int ConstructionResourcesCount,
    IReadOnlyList<string> ConstructionResourcesSample,
    IReadOnlyList<FrontierConstructionResourceItem> ConstructionResources,
    bool RequiredConstructionBlockPresent = false);
