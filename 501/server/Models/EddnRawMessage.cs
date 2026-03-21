namespace GuildDashboard.Server.Models;

/// <summary>Message EDDN stocké brut pour inspection et analyse.</summary>
/// <remarks>Pas de projection métier : capture fidèle du flux pour exploration des schémas.</remarks>
public class EddnRawMessage
{
    public long Id { get; set; }
    public string? SchemaRef { get; set; }
    public DateTime? GatewayTimestamp { get; set; }
    public string? SourceSoftware { get; set; }
    public string? SourceUploader { get; set; }
    public string? SystemName { get; set; }
    public string? StationName { get; set; }
    public string MessageJson { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
}
