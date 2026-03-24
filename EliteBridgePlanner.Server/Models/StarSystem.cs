namespace EliteBridgePlanner.Server.Models;

public class StarSystem
{
    public int Id { get; set; }

    /// <summary>Nom du système stellaire dans Elite Dangerous</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Rôle dans le pont</summary>
    public SystemType Type { get; set; }

    /// <summary>État de colonisation</summary>
    public ColonizationStatus Status { get; set; } = ColonizationStatus.PLANIFIE;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // FK Architecte — null = Inconnu
    public string? ArchitectId { get; set; }
    public AppUser? Architect { get; set; }

    // FK Pont parent    
    public  List<Bridge> Bridge { get; set; } = null!;

    // Liste chaînée
    public int? PreviousSystemId { get; set; }
    public StarSystem? PreviousSystem { get; set; }

    // Calculé dynamiquement — jamais stocké
    public bool IsStart => PreviousSystemId is null;    
    // Coordonnées pour affichage sur le frontend - herité de spansh ou EDSM - mis a jour lors de la création de système ou lors d'un refresh forcé
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
}

public enum SystemType
{
    DEBUT   = 0,
    PILE    = 1,
    TABLIER = 2,
    FIN     = 3
}

public enum ColonizationStatus
{
    PLANIFIE     = 0,
    CONSTRUCTION = 1,
    FINI         = 2
}
