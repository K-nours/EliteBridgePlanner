namespace GuildDashboard.Server.Services;

/// <summary>Normalise <c>primaryStar.type</c> EDSM pour la carte (couleur) et l'API.</summary>
public static class EdsmStarClassNormalizer
{
    /// <summary>Retourne une lettre spectrale (O–M), <c>WD</c>, <c>Neutron</c>, ou null si inconnu.</summary>
    public static string? Normalize(string? primaryStarType)
    {
        if (string.IsNullOrWhiteSpace(primaryStarType))
            return null;

        var s = primaryStarType.Trim();

        if (s.Contains("Neutron", StringComparison.OrdinalIgnoreCase))
            return "Neutron";

        if (s.Contains("White", StringComparison.OrdinalIgnoreCase) &&
            s.Contains("Dwarf", StringComparison.OrdinalIgnoreCase))
            return "WD";

        if (string.Equals(s, "WD", StringComparison.OrdinalIgnoreCase))
            return "WD";

        // Types EDSM typiques : "G (Yellow Solar) Star"
        var first = char.ToUpperInvariant(s[0]);
        if ("OBAFGKM".Contains(first))
            return first.ToString();

        for (var i = 0; i < s.Length; i++)
        {
            var c = char.ToUpperInvariant(s[i]);
            if (!"OBAFGKM".Contains(c))
                continue;
            if (i == 0 || !char.IsLetter(s[i - 1]))
                return c.ToString();
        }

        return null;
    }
}
