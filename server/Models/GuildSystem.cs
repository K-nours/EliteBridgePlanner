namespace GuildDashboard.Server.Models;

/// <summary>Système de la guilde — liste et métadonnées (source manuelle/seed).</summary>
public class GuildSystem
{
    public int Id { get; set; }
    public int GuildId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Origine | Headquarter | Guild</summary>
    public string Category { get; set; } = string.Empty;
    public decimal InfluencePercent { get; set; }
    public bool IsClean { get; set; }
    /// <summary>Type de gouvernement (ex: Feudal, Democracy).</summary>
    public string? Government { get; set; }
    /// <summary>Allégeance (ex: Empire, Federation).</summary>
    public string? Allegiance { get; set; }
    /// <summary>Powerplay (ex: Edmund Mahon). Null si indépendant.</summary>
    public string? Power { get; set; }
    /// <summary>Population du système.</summary>
    public long Population { get; set; }
    /// <summary>Nombre de factions dans le système.</summary>
    public int FactionCount { get; set; }
    /// <summary>Nombre de stations dans le système.</summary>
    public int StationCount { get; set; }
    /// <summary>Texte brut de dernière mise à jour (ex: "il y a 6 jours").</summary>
    public string? LastUpdatedText { get; set; }
    /// <summary>URL Inara du système (ex: https://inara.cz/elite/starsystem/114520/). Récupérée à l'import depuis le userscript.</summary>
    public string? InaraUrl { get; set; }
}
