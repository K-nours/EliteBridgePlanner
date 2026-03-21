namespace GuildDashboard.Server.Models;

/// <summary>Utilisateur identifié par Frontier (source d'identité).</summary>
public class FrontierUser
{
    public int Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CommanderName { get; set; } = string.Empty;
    public string? SquadronName { get; set; }
    /// <summary>Guilde associée si squadron correspond à une Guild.SquadronName.</summary>
    public int? GuildId { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Guild? Guild { get; set; }
}
