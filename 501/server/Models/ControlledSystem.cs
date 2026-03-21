namespace GuildDashboard.Server.Models;

/// <summary>Système contrôlé — données BGS pour le panneau Guild Systems.</summary>
/// <remarks>
/// InfluencePercent = influence réelle de la faction dans le système (source EDSM/Inara).
/// State = état BGS réel (War, Boom, Civil Unrest, Expansion, etc.).
/// Les valeurs actuelles sont seed/démo. Voir docs/GUILD-SYSTEMS.md pour la transition vers données réelles.
/// </remarks>
public class ControlledSystem
{
    public int Id { get; set; }
    public int GuildId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Influence réelle de la faction dans le système (%).</summary>
    public decimal InfluencePercent { get; set; }
    /// <summary>Variation d'influence sur 24h.</summary>
    public decimal? InfluenceDelta24h { get; set; }
    /// <summary>État BGS réel : War, Boom, Bust, Civil Unrest, Expansion, etc.</summary>
    public string? State { get; set; }
    /// <summary>Vrai si une autre faction est proche (écart &lt; 10%).</summary>
    public bool IsThreatened { get; set; }
    /// <summary>Vrai si influence élevée (&gt;60%) et conditions BGS compatibles avec expansion.</summary>
    public bool IsExpansionCandidate { get; set; }
    /// <summary>Vrai si la faction a la plus forte influence (contrôle le système).</summary>
    public bool IsControlled { get; set; }
    public bool IsHeadquarter { get; set; }
    /// <summary>True = données du seed/démo. False = données issues d'une sync (EDSM, etc.).</summary>
    public bool IsFromSeed { get; set; } = true;
    public bool IsClean { get; set; }
    public DateTime? LastUpdated { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Category { get; set; } = string.Empty;
}
