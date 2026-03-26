namespace GuildDashboard.Server.Services;

/// <summary>
/// Chemins du journal de vol : un sous-dossier par identité Frontier (FrontierCustomerId).
/// Données locales sous <c>Data/frontier-journal/</c> (hors git).
/// </summary>
public static class FrontierJournalStoragePaths
{
    private static readonly string[] LegacyFileNames =
    [
        "frontier-journal-raw.json",
        "frontier-journal-progress.json",
        "frontier-journal-parse-state.json",
        "frontier-journal-derived.json",
        "frontier-journal-log.json",
    ];

    public static string BaseRoot(IWebHostEnvironment env) =>
        Path.Combine(env.ContentRootPath ?? AppContext.BaseDirectory ?? ".", "Data", "frontier-journal");

    public static string SanitizeCommanderSegment(string? frontierCustomerId)
    {
        if (string.IsNullOrWhiteSpace(frontierCustomerId)) return "_unknown";
        var arr = frontierCustomerId.Trim()
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '_' : c)
            .ToArray();
        var s = new string(arr);
        return string.IsNullOrEmpty(s) ? "_unknown" : s;
    }

    public static string CommanderDirectory(IWebHostEnvironment env, string frontierCustomerId) =>
        Path.Combine(BaseRoot(env), SanitizeCommanderSegment(frontierCustomerId));

    /// <summary>
    /// Si le dossier CMDR est vide mais que d'anciens fichiers existent à la racine <c>frontier-journal</c>, les copie une fois.
    /// </summary>
    public static void TryMigrateLegacyJournalFiles(IWebHostEnvironment env, string commanderJournalDir, ILogger log)
    {
        var marker = Path.Combine(commanderJournalDir, "frontier-journal-raw.json");
        if (File.Exists(marker)) return;

        var root = BaseRoot(env);
        var legacyRaw = Path.Combine(root, "frontier-journal-raw.json");
        if (!File.Exists(legacyRaw)) return;

        Directory.CreateDirectory(commanderJournalDir);
        foreach (var name in LegacyFileNames)
        {
            var src = Path.Combine(root, name);
            var dst = Path.Combine(commanderJournalDir, name);
            if (File.Exists(src) && !File.Exists(dst))
            {
                try
                {
                    File.Copy(src, dst);
                    log.LogInformation("[FrontierJournal] Migration locale : {Name} → {Dir}", name, commanderJournalDir);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "[FrontierJournal] Migration échouée pour {Name}", name);
                }
            }
        }
    }
}
