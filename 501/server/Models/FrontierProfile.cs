namespace GuildDashboard.Server.Models;

/// <summary>Profil CAPI Frontier d'un commandant. Persistant pour affichage dashboard.</summary>
public class FrontierProfile
{
    public int Id { get; set; }
    /// <summary>ID Frontier (commander.id du CAPI).</summary>
    public string FrontierCustomerId { get; set; } = string.Empty;
    public string CommanderName { get; set; } = string.Empty;
    public string? SquadronName { get; set; }
    public string? LastSystemName { get; set; }
    public string? ShipName { get; set; }
    /// <summary>Guilde associée si squadron.name correspond à une Guild.SquadronName.</summary>
    public int? GuildId { get; set; }
    public DateTime LastFetchedAt { get; set; }

    public Guild? Guild { get; set; }
}
