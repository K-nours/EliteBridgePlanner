namespace GuildDashboard.Server.Models;

/// <summary>Système contrôlé — données BGS pour le panneau Guild Systems.</summary>
public class ControlledSystem
{
    public int Id { get; set; }
    public int GuildId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal InfluencePercent { get; set; }
    public decimal? InfluenceDelta24h { get; set; }
    public string? State { get; set; }
    public bool IsThreatened { get; set; }
    public bool IsExpansionCandidate { get; set; }
    public bool IsHeadquarter { get; set; }
    public bool IsClean { get; set; }
    public DateTime? LastUpdated { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Category { get; set; } = string.Empty;
}
