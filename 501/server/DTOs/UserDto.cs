namespace GuildDashboard.Server.DTOs;

/// <summary>Utilisateur courant (identité Frontier).</summary>
public record FrontierUserDto(
    string CustomerId,
    string Commander,
    string? Squadron,
    int? GuildId,
    string? GuildName
);
