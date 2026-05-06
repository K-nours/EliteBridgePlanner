namespace GuildDashboard.Server.DTOs;

/// <summary>
/// Inventaire commodités CMDR (CAPI /fleetcarrier soute FC uniquement — soute vaisseau non disponible via CAPI).
/// </summary>
public sealed class FrontierLogisticsInventoryDto
{
    /// <summary>Quantités par nom de commodité (agrégation insensible à la casse côté serveur).</summary>
    public Dictionary<string, int> CarrierCargoByName { get; set; } = new();

    public string FetchedAtUtc { get; set; } = DateTime.UtcNow.ToString("O");

    /// <summary>Erreur ou absence de FC (null si OK ou pas de FC — pas une erreur bloquante).</summary>
    public string? CarrierCargoError { get; set; }

    /// <summary>True si /fleetcarrier a répondu HTTP 429.</summary>
    public bool CarrierRateLimited { get; set; }

    /// <summary>Au moins un appel CAPI en rate limit sur ce cycle.</summary>
    public bool RateLimited { get; set; }

    /// <summary>Secondes suggérées avant retry (Retry-After ou défaut serveur).</summary>
    public int? RetryAfterSeconds { get; set; }
}
