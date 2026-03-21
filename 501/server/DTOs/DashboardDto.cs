namespace GuildDashboard.Server.DTOs;

public record DashboardResponseDto(
    string FactionName,
    string SquadronName,
    string? CurrentCommanderName,
    IReadOnlyList<CmdrDto> Cmdrs,
    FrontierProfileDto? FrontierProfile
);

public record CmdrDto(string Name, string? AvatarUrl, bool IsCurrentUser);
