namespace GuildDashboard.Server.Models;

/// <summary>Système de la guilde — liste et catégorie (Origin vs autres).</summary>
public class GuildSystem
{
    public int Id { get; set; }
    public int GuildId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Origine | Headquarter | Guild</summary>
    public string Category { get; set; } = string.Empty;
    public decimal InfluencePercent { get; set; }
    public bool IsClean { get; set; }
}
