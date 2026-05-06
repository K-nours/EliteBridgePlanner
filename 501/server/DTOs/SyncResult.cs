namespace GuildDashboard.Server.DTOs;

/// <summary>Résultat d'une synchronisation Inara.</summary>
public record SquadronSyncResult(int SyncedCount, string? Error = null)
{
    public bool IsSuccess => Error == null;
    public static SquadronSyncResult Failure(string error) => new(0, error);
}
