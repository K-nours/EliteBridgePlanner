namespace GuildDashboard.Server.Services;

/// <summary>
/// Stockage en mémoire de la progression EDSM (job enrich-edsm séparé).
/// Thread-safe.
/// </summary>
public class SystemsImportProgressStore
{
    private readonly object _lock = new();
    private ProgressData? _data;
    private int? _guildId;

    /// <summary>Met à jour la progression. Phase = "edsm" | "done". Status = "envoi" | "réception" | "analyse" (optionnel).</summary>
    public void Set(int guildId, string phase, string mode, int current, int total, int? enrichedCount = null, string? error = null, string? status = null, int? displayableCount = null, int? ignoredCount = null)
    {
        lock (_lock)
        {
            _guildId = guildId;
            _data = new ProgressData(phase, mode, current, total, enrichedCount, error, status, displayableCount, ignoredCount);
        }
    }

    /// <summary>Récupère la progression. Retourne null si aucun job en cours pour ce guildId.</summary>
    public (string Phase, string Mode, int Current, int Total, int? EnrichedCount, string? Error, string? Status, int? DisplayableCount, int? IgnoredCount)? Get(int guildId)
    {
        lock (_lock)
        {
            if (_guildId != guildId || _data == null) return null;
            return (_data.Phase, _data.Mode, _data.Current, _data.Total, _data.EnrichedCount, _data.Error, _data.Status, _data.DisplayableCount, _data.IgnoredCount);
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

    private sealed class ProgressData
    {
        public string Phase { get; }
        public string Mode { get; }
        public int Current { get; }
        public int Total { get; }
        public int? EnrichedCount { get; }
        public string? Error { get; }
        public string? Status { get; }
        public int? DisplayableCount { get; }
        public int? IgnoredCount { get; }

        public ProgressData(string phase, string mode, int current, int total, int? enrichedCount, string? error, string? status, int? displayableCount, int? ignoredCount)
        {
            Phase = phase;
            Mode = mode;
            Current = current;
            Total = total;
            EnrichedCount = enrichedCount;
            Error = error;
            Status = status;
            DisplayableCount = displayableCount;
            IgnoredCount = ignoredCount;
        }
    }
}
