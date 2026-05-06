namespace GuildDashboard.Server.Data;

/// <summary>Seuils d'influence pour l'affichage. Source unique backend — frontend utilise ces valeurs.</summary>
/// <remarks>
/// - &lt; Critical = rouge vif (critique)
/// - &lt; Low = rouge (basse influence)
/// - &gt;= High = vert (haute influence)
/// - Entre Low et High = normal
/// </remarks>
public static class InfluenceThresholds
{
    /// <summary>&lt; 10% → critique.</summary>
    public const decimal Critical = 10;

    /// <summary>&lt; 30% → basse influence.</summary>
    public const decimal Low = 30;

    /// <summary>≥ 60% → haute influence.</summary>
    public const decimal High = 60;

    /// <summary>Retourne la classe CSS/affichage pour un pourcentage.</summary>
    public static string GetInfluenceClass(decimal influencePercent)
    {
        if (influencePercent < Critical) return "influence-critical";
        if (influencePercent < Low) return "influence-low";
        if (influencePercent >= High) return "influence-high";
        return "influence-normal";
    }
}
