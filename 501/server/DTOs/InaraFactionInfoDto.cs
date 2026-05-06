namespace GuildDashboard.Server.DTOs;

/// <summary>Informations sur une faction récupérées depuis Inara (scraping chaîné : système → faction → escadron).</summary>
public sealed class InaraFactionInfoDto
{
    public string? FactionName { get; set; }
    public string? FactionInaraUrl { get; set; }

    /// <summary>Allégeance (ex. Indépendants, Empire, Fédération).</summary>
    public string? Allegiance { get; set; }

    /// <summary>Gouvernement (ex. Démocratie, Corporation).</summary>
    public string? Government { get; set; }

    /// <summary>Système d'origine (ex. HR 5451).</summary>
    public string? Origin { get; set; }

    /// <summary>True si la faction est une faction de joueur.</summary>
    public bool? IsPlayerFaction { get; set; }

    // Escadron associé (présent seulement si la faction est liée à un escadron)
    public string? SquadronName { get; set; }
    public string? SquadronInaraUrl { get; set; }
    public string? SquadronLanguage { get; set; }
    public string? SquadronTimezone { get; set; }
    public int? SquadronMembersCount { get; set; }

    /// <summary>Message d'erreur si le scraping a échoué (anti-bot, timeout, structure DOM modifiée).</summary>
    public string? Error { get; set; }
}
