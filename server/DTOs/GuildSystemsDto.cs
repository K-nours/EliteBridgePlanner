namespace GuildDashboard.Server.DTOs;

public record GuildSystemBgsDto(
    int Id,
    string Name,
    decimal InfluencePercent,
    decimal? InfluenceDelta24h,
    string? State,
    bool IsThreatened,
    bool IsExpansionCandidate,
    bool IsClean,
    string Category,
    DateTime? LastUpdated
);

public record GuildSystemsResponseDto(
    IReadOnlyList<GuildSystemBgsDto> Origin,
    IReadOnlyList<GuildSystemBgsDto> Headquarter,
    IReadOnlyList<GuildSystemBgsDto> Others
);
