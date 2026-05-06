namespace GuildDashboard.Server.DTOs;

/// <summary>Payload pour mise à jour des paramètres guild (URLs Inara).</summary>
public class GuildSettingsUpdateDto
{
    public string? InaraFactionPresenceUrl { get; set; }
    public string? InaraSquadronUrl { get; set; }
    public string? InaraCmdrUrl { get; set; }
}
