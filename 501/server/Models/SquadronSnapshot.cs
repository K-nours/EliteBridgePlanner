namespace GuildDashboard.Server.Models;

/// <summary>Métadonnées de sync du roster Inara (pour badge live/cached).</summary>
public class SquadronSnapshot
{
    public int Id { get; set; }
    public int GuildId { get; set; }
    public DateTime LastSyncedAt { get; set; }
    public bool Success { get; set; }
    public int? MembersCount { get; set; }

    public Guild Guild { get; set; } = null!;
}
