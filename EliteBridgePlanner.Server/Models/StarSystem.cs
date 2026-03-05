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
    public int BridgeId { get; set; }
    public Bridge Bridge { get; set; } = null!;

    // Liste chaînée
    public int? PreviousSystemId { get; set; }
    public StarSystem? PreviousSystem { get; set; }

    // Calculé dynamiquement — jamais stocké
    public bool IsStart => PreviousSystemId is null;    
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
