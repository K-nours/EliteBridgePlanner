namespace GuildDashboard.Server.Integrations.Eddn;

/// <summary>Couverture des champs par schéma EDDN — pour identifier les schémas utiles au BGS.</summary>
public class EddnSchemaCoverage
{
    public string SchemaRef { get; set; } = string.Empty;
    public long TotalCount { get; set; }
    public long WithSystemName { get; set; }
    public long WithStationName { get; set; }
    public bool HasFactions { get; set; }
    public bool HasInfluence { get; set; }
    public bool HasState { get; set; }
}
