namespace GuildDashboard.Server.Services;

/// <summary>
/// Stockage en mémoire de la progression d'un import Systems (phase EDSM).
/// Effacé à la fin de l'import. Thread-safe.
/// </summary>
public class SystemsImportProgressStore
{
    private readonly object _lock = new();
    private (string Phase, string Mode, int Current, int Total)? _data;
    private int? _guildId;

    /// <summary>Met à jour la progression pour un guildId. Phase = "edsm" | "inara" | "done". Mode = "groupée" | "unitaire".</summary>
    public void Set(int guildId, string phase, string mode, int current, int total)
    {
        lock (_lock)
        {
            _guildId = guildId;
            _data = (phase, mode, current, total);
        }
    }

    /// <summary>Récupère la progression pour un guildId. Retourne null si aucune progression en cours ou guildId différent.</summary>
    public (string Phase, string Mode, int Current, int Total)? Get(int guildId)
    {
        lock (_lock)
        {
            if (_guildId != guildId || !_data.HasValue) return null;
            return _data.Value;
        }
    }

    /// <summary>Efface la progression. Appelé à la fin de l'import.</summary>
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
