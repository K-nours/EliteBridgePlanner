using System.Text.RegularExpressions;

namespace GuildDashboard.Server.Data;

/// <summary>Normalise les noms de systèmes Elite (ex: Hip4332 → Hip 4332) pour éviter les doublons.</summary>
public static class SystemNameNormalizer
{
    /// <summary>Hip4332 → Hip 4332, HIP 4332 → Hip 4332. Insère un espace entre mot et chiffres quand absent.</summary>
    private static readonly Regex HipPattern = new(@"^(HIP|Hip|hip)\s*(\d+)$", RegexOptions.Compiled);

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var s = name.Trim();
        var m = HipPattern.Match(s);
        if (m.Success)
            return "Hip " + m.Groups[2].Value;
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
