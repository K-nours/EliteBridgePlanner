namespace GuildDashboard.Server.DTOs;

/// <summary>Entrée du pipeline diplomatique : système critique + faction dominante EDSM.</summary>
public sealed class DiplomaticPipelineEntryDto
{
    public string SystemName { get; set; } = "";
    /// <summary>Influence de la guilde dans ce système (< 5% → critique).</summary>
    public decimal GuildInfluencePercent { get; set; }
    /// <summary>Faction contrôlante (= dominante) selon EDSM. Null si EDSM ne renvoie pas le système.</summary>
    public string? DominantFaction { get; set; }
    /// <summary>État de la faction dominante (ex. War, None). Null si EDSM ne renvoie pas.</summary>
    public string? DominantFactionState { get; set; }
}

/// <summary>Résultat complet du pipeline diplomatique.</summary>
public sealed class DiplomaticPipelineDto
{
    /// <summary>Systèmes critiques, triés par influence croissante (les plus urgents en premier).</summary>
    public List<DiplomaticPipelineEntryDto> Entries { get; set; } = new();
    public string FetchedAtUtc { get; set; } = DateTime.UtcNow.ToString("O");
    /// <summary>False si EDSM n'a pu être joint (erreur réseau) — les Entries peuvent être incomplètes.</summary>
    public bool EdsmAvailable { get; set; } = true;
}
