namespace GuildDashboard.Server.DTOs;

/// <summary>Profil Frontier exposé au dashboard (CMDR, système, vaisseau, squadron, guild).</summary>
public record FrontierProfileDto(
    string FrontierCustomerId,
    string CommanderName,
    string? SquadronName,
    string? LastSystemName,
    string? ShipName,
    int? GuildId,
    string? GuildName,
    DateTime LastFetchedAt
);
