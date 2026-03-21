namespace GuildDashboard.Server.DTOs;

/// <summary>Payload d'import CMDRs (userscript Inara squadron-roster).</summary>
public record CommandersImportPayload(
    IReadOnlyList<CommanderImportItem> Commanders
);

/// <summary>Un CMDR extrait depuis la page roster Inara.</summary>
public record CommanderImportItem(
    string Name,
    string? Role
);

/// <summary>Payload d'import avatar depuis la page CMDR Inara.</summary>
public record AvatarImportPayload(
    string AvatarUrl,
    string? CommanderName
);

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
