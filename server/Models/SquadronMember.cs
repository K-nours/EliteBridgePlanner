namespace GuildDashboard.Server.Models;

/// <summary>CommanderSnapshot — cache des membres du squadron, mis à jour par sync Inara.</summary>
public class SquadronMember
{
    public int Id { get; set; }
    public int GuildId { get; set; }
    public string CommanderName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? Role { get; set; }
    public DateTime? LastSyncedAt { get; set; }

    public Guild Guild { get; set; } = null!;
}
