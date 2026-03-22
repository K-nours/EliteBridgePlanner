namespace GuildDashboard.Server.DTOs;

public record GuildSystemBgsDto(
    int Id,
    string Name,
    decimal InfluencePercent,
    decimal? InfluenceDelta24h,
    string? State,
    bool IsThreatened,
    bool IsExpansionCandidate,
    bool IsHeadquarter,
    bool IsClean,
    string Category,
    DateTime? LastUpdated,
    bool IsFromSeed
);

public record GuildSystemsResponseDto(
    IReadOnlyList<GuildSystemBgsDto> Origin,
    IReadOnlyList<GuildSystemBgsDto> Headquarter,
    IReadOnlyList<GuildSystemBgsDto> Others,
    string DataSource // "seed" | "cached" — jamais "live" sans sync réelle
);

/// <summary>Entrée d'audit pour un système : valeurs stockées, DTO, catégorie et classe influence.</summary>
public record GuildSystemAuditEntry(
    string RequestedName,
    bool Found,
    int? GuildSystemId,
    decimal? GuildSystemInfluencePercent,
    string? GuildSystemCategory,
    decimal? ControlledSystemInfluencePercent,
    string? ControlledSystemState,
    bool? ControlledSystemIsThreatened,
    bool? ControlledSystemIsExpansionCandidate,
    bool? ControlledSystemIsHeadquarter,
    decimal? DtoInfluencePercent,
    string? DtoState,
    string CategoryDisplay,
    string InfluenceClass,
    string SourceUsed // "GuildSystem" — source de vérité pour l'affichage
);
