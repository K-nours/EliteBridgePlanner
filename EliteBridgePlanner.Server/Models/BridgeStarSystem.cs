namespace EliteBridgePlanner.Server.Models;

/// <summary>
/// Entité de jonction reliant un StarSystem à un Bridge.
/// Permet à un système stellaire d'être présent dans plusieurs ponts
/// avec des rôles (Type) et des états (Status) différents.
/// </summary>
public class BridgeStarSystem
{
    public int Id { get; set; }

    // FKs
    public int BridgeId { get; set; }
    public int StarSystemId { get; set; }

    // Navigation properties
    public Bridge Bridge { get; set; } = null!;
    public StarSystem StarSystem { get; set; } = null!;

    // Propriétés de la relation
    /// <summary>Rôle du système dans ce pont spécifique</summary>
    public SystemType Type { get; set; }

    /// <summary>Chaînage au sein de ce pont spécifique</summary>
    public int? PreviousSystemId { get; set; }
    public BridgeStarSystem? PreviousSystem { get; set; }

    /// <summary>Timestamps</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Calculé — aucun prédécesseur = système de départ du pont</summary>
    public bool IsStart => PreviousSystemId is null;
}

public enum SystemType
{
    DEBUT = 0,
    PILE = 1,
    TABLIER = 2,
    FIN = 3
}
