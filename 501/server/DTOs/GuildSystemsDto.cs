namespace GuildDashboard.Server.DTOs;

public record GuildSystemBgsDto(
    int Id,
    string Name,
    decimal InfluencePercent,
    decimal? InfluenceDelta24h,
    string? State,
    IReadOnlyList<string>? States,
    bool IsThreatened,
    bool IsExpansionCandidate,
    bool IsHeadquarter,
    bool IsUnderSurveillance,
    bool IsClean,
    string Category,
    DateTime? LastUpdated,
    bool IsFromSeed
);

/// <summary>Seuils d'influence (source unique backend).</summary>
public record InfluenceThresholdsDto(decimal Critical, decimal Low, decimal High);

/// <summary>Seuils tactiques pour catégorisation (Critical &lt; 5%, Low &lt; 15%, Healthy ≥ 60%).</summary>
public record TacticalThresholdsDto(decimal Critical, decimal Low, decimal High);

public record GuildSystemsResponseDto(
    IReadOnlyList<GuildSystemBgsDto> Origin,
    IReadOnlyList<GuildSystemBgsDto> Headquarter,
    IReadOnlyList<GuildSystemBgsDto> Surveillance,
    IReadOnlyList<GuildSystemBgsDto> Conflicts,
    IReadOnlyList<GuildSystemBgsDto> Critical,
    IReadOnlyList<GuildSystemBgsDto> Low,
    IReadOnlyList<GuildSystemBgsDto> Healthy,
    IReadOnlyList<GuildSystemBgsDto> Others,
    string DataSource, // "seed" | "cached" — jamais "live" sans sync réelle
    InfluenceThresholdsDto InfluenceThresholds,
    TacticalThresholdsDto TacticalThresholds
);

/// <summary>Entrée d'audit pour un système : Inara, GuildSystem, ControlledSystem, DTO final. Diagnostic catégorisation et influence.</summary>
public record GuildSystemAuditEntry(
    string RequestedName,
    bool Found,
    decimal? InaraInfluencePercent,
    decimal? RawInaraInfluence,      // alias InaraInfluencePercent (valeur Inara API)
    decimal? ParsedInfluence,       // = GuildSystemInfluencePercent (résultat InfluenceParse)
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
    string CategoryDisplay,         // libellé: Origine | Quartier général | Systèmes critiques | Autres
    string FinalDisplayCategory,    // clé: origin | headquarter | critical | others
    string InfluenceClass,
    string SourceUsed,
    string? PayloadCategory = null  // null à l'audit (uniquement pendant import, voir logs)
);
