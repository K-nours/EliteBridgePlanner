using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Clé API Inara (INAPI) — fichier local <c>Data/inara-api-user.json</c> (rempli via la modal Paramètres dans l’app, fichier listé dans <c>.gitignore</c>).
/// Repli optionnel : variable d’environnement ou secret hors dépôt (<c>Inara__ApiKey</c> / <c>Inara:ApiKey</c> dans la config fusionnée).
/// Ne pas stocker de clé dans <c>appsettings*.json</c> versionné.
/// </summary>
public sealed class InaraApiUserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _lock = new();
    private readonly ILogger<InaraApiUserSettingsStore> _log;

    public InaraApiUserSettingsStore(IWebHostEnvironment env, ILogger<InaraApiUserSettingsStore> log)
    {
        var dir = Path.Combine(env.ContentRootPath ?? AppContext.BaseDirectory ?? ".", "Data");
        _path = Path.Combine(dir, "inara-api-user.json");
        _log = log;
    }

    public InaraApiUserSettingsFile? Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
                return null;
            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<InaraApiUserSettingsFile>(json);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[Inara API settings] Lecture impossible: {Path}", _path);
                return null;
            }
        }
    }

    public void Save(InaraApiUserSettingsFile settings)
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

    /// <summary>Fichier local d’abord, sinon valeur dans la configuration fusionnée (ex. env <c>Inara__ApiKey</c>), jamais attendue depuis appsettings commités.</summary>
    public string? ResolveApiKey(IConfiguration config)
    {
        var file = Load();
        if (!string.IsNullOrWhiteSpace(file?.ApiKey))
            return file!.ApiKey.Trim();
        var cfg = config["Inara:ApiKey"];
        return string.IsNullOrWhiteSpace(cfg) ? null : cfg.Trim();
    }
}

public sealed class InaraApiUserSettingsFile
{
    public string ApiKey { get; set; } = "";
}
