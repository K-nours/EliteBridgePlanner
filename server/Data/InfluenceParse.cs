using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuildDashboard.Server.Data;

/// <summary>Parsing strict des pourcentages d'influence (0–100). Garde uniquement chiffres + séparateur décimal.</summary>
public static class InfluenceParse
{
    /// <summary>Caractères autorisés pour le parsing : chiffres, virgule, point.</summary>
    private static readonly Regex StrictPercentRegex = new(@"^\s*([\d]+[,.]?[\d]*)\s*%?\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Parse une chaîne en pourcentage. Supporte "23.6%", "23,6%", "23.6", "23,6".
    /// Retourne 0 si invalide. Clamp à [0, 100]. Log un warning si la valeur parsée est &gt; 100.
    /// </summary>
    public static decimal ParseStrict(string? value, ILogger? log = null, string? source = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;

        var s = value.Trim();
        // Remplacer virgule par point pour parsing
        s = s.Replace(",", ".");
        // Retirer le % en fin
        s = s.TrimEnd('%').Trim();

        // Garder uniquement chiffres et un séparateur décimal
        if (!Regex.IsMatch(s, @"^[\d.]+$"))
        {
            // Fallback : extraire le premier nombre trouvé (strict)
            var m = Regex.Match(s, @"([\d]+\.?[\d]*)");
            if (!m.Success) return 0;
            s = m.Groups[1].Value;
        }

        if (!decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return 0;

        if (parsed > 100)
        {
            log?.LogWarning("[InfluenceParse] Valeur > 100 détectée et corrigée: raw=\"{Raw}\" parsed={Parsed} source={Source}",
                value, parsed, source ?? "?");
            return 100;
        }
        if (parsed < 0) return 0;

        return Math.Round(parsed, 2);
    }

    /// <summary>Parse un JsonElement (number ou string) en pourcentage.</summary>
    public static decimal ParseStrict(JsonElement je, ILogger? log = null, string? source = null)
    {
        if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined) return 0;
        if (je.ValueKind == JsonValueKind.Number)
        {
            var num = je.GetDecimal();
            if (num > 100)
            {
                log?.LogWarning("[InfluenceParse] Valeur > 100 (JSON number): parsed={Parsed} source={Source}", num, source ?? "?");
                return 100;
            }
            return num < 0 ? 0 : Math.Round(num, 2);
        }
        var str = je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
        return ParseStrict(str, log, source);
    }

    /// <summary>Sanitise une valeur déjà parsée : si > 100, retourne 0 (corrompu).</summary>
    public static decimal Sanitize(decimal value)
    {
        if (value > 100 || value < 0) return 0;
        return Math.Round(value, 2);
    }
}
