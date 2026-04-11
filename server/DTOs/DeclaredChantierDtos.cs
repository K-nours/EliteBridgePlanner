namespace GuildDashboard.Server.DTOs;

public record DeclaredChantierPersistRequest(
    string SystemName,
    string StationName,
    string? MarketId,
    string? CommanderName,
    IReadOnlyList<DeclaredChantierResourceDto>? ConstructionResources,
    int? ConstructionResourcesTotal);

/// <summary>POST chantiers-declared/refresh-one — chantier ciblé par id SQL.</summary>
public record DeclaredChantierRefreshOneRequest(int Id);

/// <summary>Résumé POST chantiers-declared/refresh-all (pas de JSON massif).</summary>
public record DeclaredChantierRefreshAllResultDto(
    int Updated,
    int Deactivated,
    int Skipped,
    long ElapsedMs,
    string? Note);

public record DeclaredChantierResourceDto(
    string Name,
    long Required,
    long Provided,
    long Remaining);

/// <summary>Liste GET — pas de JSON brut marché, champs stables pour l’UI.</summary>
public record DeclaredChantierListItemDto(
    int Id,
    string CmdrName,
    string SystemName,
    string StationName,
    string? MarketId,
    bool Active,
    DateTime DeclaredAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<DeclaredChantierResourceDto> ConstructionResources,
    int ConstructionResourcesTotal);
