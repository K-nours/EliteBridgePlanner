namespace GuildDashboard.Server.Models;

/// <summary>Chantier de construction déclaré (docké) pour la guilde courante.</summary>
public class DeclaredChantier
{
    public int Id { get; set; }
    public int GuildId { get; set; }

    public string CmdrName { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;

    /// <summary>Identifiant marché CAPI si disponible (ex. id racine / marketId).</summary>
    public string? MarketId { get; set; }

    /// <summary>Clés normalisées pour unicité sans MarketId (minuscules, trim).</summary>
    public string SystemNameKey { get; set; } = string.Empty;
    public string StationNameKey { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    /// <summary>Première déclaration (inchangé à l’upsert).</summary>
    public DateTime DeclaredAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Snapshot compact des fournitures (JSON array), optionnel.</summary>
    public string? ConstructionResourcesJson { get; set; }

    public Guild? Guild { get; set; }
}
