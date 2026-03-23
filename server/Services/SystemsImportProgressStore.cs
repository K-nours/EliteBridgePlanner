namespace GuildDashboard.Server.Services;

/// <summary>
/// Stockage en mémoire de la progression EDSM (job enrich-edsm séparé).
/// Thread-safe.
/// </summary>
public class SystemsImportProgressStore
{
    private readonly object _lock = new();
    private (string Phase, string Mode, int Current, int Total, int? EnrichedCount, string? Error)? _data;
    private int? _guildId;

    /// <summary>Met à jour la progression. Phase = "edsm" | "done". Mode = "groupée" | "unitaire".</summary>
    public void Set(int guildId, string phase, string mode, int current, int total, int? enrichedCount = null, string? error = null)
    {
        lock (_lock)
        {
            _guildId = guildId;
            _data = (phase, mode, current, total, enrichedCount, error);
        }
    }

    /// <summary>Récupère la progression. Retourne null si aucun job en cours pour ce guildId.</summary>
    public (string Phase, string Mode, int Current, int Total, int? EnrichedCount, string? Error)? Get(int guildId)
    {
        lock (_lock)
        {
            if (_guildId != guildId || !_data.HasValue) return null;
            return _data.Value;
        }
    }

    /// <summary>Efface la progression. Appelé après que le dashboard ait récupéré le résultat final.</summary>
    public void Clear(int guildId)
    {
        lock (_lock)
        {
            if (_guildId == guildId)
            {
                _guildId = null;
                _data = null;
            }
        }
    }
}
