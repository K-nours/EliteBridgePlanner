namespace GuildDashboard.Server.Models;

/// <summary>Guilde — champs minimaux pour Guild Systems.</summary>
public class Guild
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? SquadronName { get; set; }
    public string? FactionName { get; set; }
    public int? InaraFactionId { get; set; }
    /// <summary>ID du squadron sur Inara (ex: 4926) — utilisé pour fetcher le roster public.</summary>
    public int? InaraSquadronId { get; set; }

    public ICollection<SquadronMember> SquadronMembers { get; set; } = new List<SquadronMember>();
    public ICollection<SquadronSnapshot> SquadronSnapshots { get; set; } = new List<SquadronSnapshot>();
}
