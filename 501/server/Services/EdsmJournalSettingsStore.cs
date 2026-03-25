using System.Text.Json;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Paramètres EDSM (journal) persistés hors dépôt : <c>Data/edsm-journal-user.json</c>.
/// Complète ou remplace Edsm:CommanderName / Edsm:ApiKey dans appsettings ou user-secrets.
/// </summary>
public sealed class EdsmJournalSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _lock = new();
    private readonly ILogger<EdsmJournalSettingsStore> _log;

    public EdsmJournalSettingsStore(IWebHostEnvironment env, ILogger<EdsmJournalSettingsStore> log)
    {
        var dir = Path.Combine(env.ContentRootPath ?? AppContext.BaseDirectory ?? ".", "Data");
        _path = Path.Combine(dir, "edsm-journal-user.json");
        _log = log;
    }

    public EdsmJournalUserSettingsFile? Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
                return null;
            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<EdsmJournalUserSettingsFile>(json);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[EDSM settings] Lecture impossible: {Path}", _path);
                return null;
            }
        }
    }

    public void Save(EdsmJournalUserSettingsFile settings)
    {
        lock (_lock)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
    }
}

public sealed class EdsmJournalUserSettingsFile
{
    public string CommanderName { get; set; } = "";
    public string ApiKey { get; set; } = "";
}
