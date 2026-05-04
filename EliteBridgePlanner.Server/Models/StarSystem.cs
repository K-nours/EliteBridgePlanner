namespace EliteBridgePlanner.Server.Models;

public class StarSystem
{
    public int Id { get; set; }

    /// <summary>Nom du système stellaire dans Elite Dangerous — UNIQUE et utilisé comme clé de recherche</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Architecte qui gère ce système</summary>
    public string? ArchitectId { get; set; }
    public AppUser? Architect { get; set; }

    /// <summary>État de colonisation du système dans ce pont spécifique</summary>
    public ColonizationStatus Status { get; set; } = ColonizationStatus.PLANIFIE;

    /// <summary>Coordonnées pour affichage sur le frontend — héritées de Spansh ou EDSM</summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation — Relations vers les ponts via la jonction
    public ICollection<BridgeStarSystem> BridgeAssociations { get; set; } = [];
}



public enum ColonizationStatus
{
    PLANIFIE     = 0,
    CONSTRUCTION = 1,
    FINI         = 2
}
