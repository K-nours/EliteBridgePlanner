using Microsoft.AspNetCore.Identity;

namespace EliteBridgePlanner.Server.Models;

/// <summary>
/// Utilisateur de l'application.
/// CommanderName = alias CMDR Elite Dangerous.
/// Conçu pour migrer vers Frontier SSO : ajouter FrontierId + mapper le sub claim.
/// </summary>
public class AppUser : IdentityUser
{
    public string CommanderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Préférences i18n
    public string PreferredLanguage { get; set; } = "en-GB";
    public string PreferredTimeZone { get; set; } = "UTC";

    // Navigation
    public ICollection<StarSystem> ArchitectedSystems { get; set; } = [];
    public ICollection<Bridge> CreatedBridges { get; set; } = [];
}
