namespace GuildDashboard.Server.DTOs;

/// <summary>Réponse GET /api/dashboard/commanders</summary>
public record CommandersResponseDto(
    IReadOnlyList<CommanderDto> Commanders,
    DateTime? LastSyncedAt,
    string DataSource
);

/// <summary>Membre du squadron (cache)</summary>
public record CommanderDto(
    string Name,
    string? AvatarUrl,
    string? Role,
    DateTime? LastSyncedAt
);
