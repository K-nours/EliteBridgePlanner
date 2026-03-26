using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Archive ZIP du journal Frontier local (par CMDR) : export / import avec validation d’identité.
/// </summary>
public sealed class FrontierJournalImportExportService
{
    public const string ManifestFileName = "journal-export-manifest.json";
    public const string FormatId = "elitebridge-frontier-journal";

    private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions JsonWrite = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly HashSet<string> JournalDataFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        FrontierJournalBackfillService.RawFileName,
        "frontier-journal-progress.json",
        "frontier-journal-parse-state.json",
        "frontier-journal-derived.json",
        "frontier-journal-log.json",
    };

    private readonly IWebHostEnvironment _env;
    private readonly FrontierUserService _users;
    private readonly FrontierJournalBackfillService _backfill;
    private readonly FrontierJournalUnifiedSyncService _unified;
    private readonly ILogger<FrontierJournalImportExportService> _log;

    public FrontierJournalImportExportService(
        IWebHostEnvironment env,
        FrontierUserService users,
        FrontierJournalBackfillService backfill,
        FrontierJournalUnifiedSyncService unified,
        ILogger<FrontierJournalImportExportService> log)
    {
        _env = env;
        _users = users;
        _backfill = backfill;
        _unified = unified;
        _log = log;
    }

    /// <summary>Crée une archive ZIP du dossier journal du CMDR courant, ou null si pas de profil.</summary>
    public async Task<(byte[] ZipBytes, string DownloadFileName)?> ExportAsync(CancellationToken ct)
    {
        var profile = await _users.GetProfileAsync(ct);
        if (profile == null || string.IsNullOrWhiteSpace(profile.FrontierCustomerId))
            return null;

        var commanderDir = FrontierJournalStoragePaths.CommanderDirectory(_env, profile.FrontierCustomerId);
        FrontierJournalStoragePaths.TryMigrateLegacyJournalFiles(_env, commanderDir, _log);
        Directory.CreateDirectory(commanderDir);

        var included = new List<string>();
        await using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = new FrontierJournalExportManifestDto
            {
                Format = FormatId,
                SchemaVersion = 1,
                CommanderName = profile.CommanderName ?? "",
                FrontierCustomerId = profile.FrontierCustomerId,
                ExportedAt = DateTime.UtcNow,
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev",
            };

            foreach (var name in JournalDataFileNames)
            {
                ct.ThrowIfCancellationRequested();
                var path = Path.Combine(commanderDir, name);
                if (!File.Exists(path)) continue;
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var es = entry.Open();
                await using var fs = File.OpenRead(path);
                await fs.CopyToAsync(es, ct);
                included.Add(name);
            }

            manifest.Files = included;
            var manifestEntry = zip.CreateEntry(ManifestFileName, CompressionLevel.Optimal);
            await using (var mw = manifestEntry.Open())
            {
                var json = JsonSerializer.Serialize(manifest, JsonWrite);
                await mw.WriteAsync(Encoding.UTF8.GetBytes(json), ct);
            }
        }

        var safeCmdr = SanitizeFileSegment(profile.CommanderName ?? "cmdr");
        var fileName = $"frontier-journal-{safeCmdr}-{DateTime.UtcNow:yyyyMMdd-HHmmss}Z.zip";
        return (ms.ToArray(), fileName);
    }

    public async Task<FrontierJournalImportResultDto> ImportAsync(
        Stream zipStream,
        string strategy,
        string duplicatePolicy,
        CancellationToken ct)
    {
        var normalizedStrategy = (strategy ?? "").Trim().ToLowerInvariant();
        if (normalizedStrategy is not ("replace" or "merge"))
        {
            return new FrontierJournalImportResultDto
            {
                Success = false,
                Message = "Stratégie invalide : utiliser replace ou merge.",
            };
        }

        var dup = (duplicatePolicy ?? "skip").Trim().ToLowerInvariant();
        if (dup is not ("skip" or "import"))
        {
            return new FrontierJournalImportResultDto
            {
                Success = false,
                Message = "duplicatePolicy invalide : skip ou import.",
            };
        }

        if (_unified.GetStatusSnapshot().IsRunning)
        {
            return new FrontierJournalImportResultDto
            {
                Success = false,
                Message = "Une synchronisation du journal est en cours. Arrêtez-la avant d’importer.",
            };
        }

        await using var buffered = new MemoryStream();
        await zipStream.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        var profile = await _users.GetProfileAsync(ct);
        if (profile == null || string.IsNullOrWhiteSpace(profile.FrontierCustomerId))
        {
            return new FrontierJournalImportResultDto
            {
                Success = false,
                Message = "Aucun CMDR Frontier identifié. Connectez-vous d’abord.",
            };
        }

        FrontierJournalExportManifestDto? manifest;
        Dictionary<string, byte[]> filesFromZip;
        try
        {
            (manifest, filesFromZip) = ReadAndValidateZip(buffered);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[FrontierJournal] Import : ZIP ou manifest invalide");
            return new FrontierJournalImportResultDto
            {
                Success = false,
                Message = "Fichier invalide : archive ZIP attendue, avec manifest correct.",
            };
        }

        if (manifest == null || manifest.Format != FormatId || manifest.SchemaVersion != 1)
        {
            return new FrontierJournalImportResultDto
            {
                Success = false,
                Message = "Manifest d’export non reconnu (format ou version).",
            };
        }

        if (!string.Equals(
                FrontierJournalStoragePaths.SanitizeCommanderSegment(manifest.FrontierCustomerId),
                FrontierJournalStoragePaths.SanitizeCommanderSegment(profile.FrontierCustomerId),
                StringComparison.Ordinal))
        {
            return new FrontierJournalImportResultDto
            {
                Success = false,
                Message = "Cette sauvegarde appartient à un autre compte Frontier (FrontierCustomerId différent). Import annulé.",
            };
        }

        if (!CommanderNamesMatch(manifest.CommanderName, profile.CommanderName))
        {
            return new FrontierJournalImportResultDto
            {
                Success = false,
                Message =
                    $"Le nom CMDR dans la sauvegarde ({manifest.CommanderName}) ne correspond pas au compte connecté ({profile.CommanderName}). Import annulé.",
            };
        }

        var commanderDir = FrontierJournalStoragePaths.CommanderDirectory(_env, profile.FrontierCustomerId);
        Directory.CreateDirectory(commanderDir);

        if (normalizedStrategy == "replace")
            return await ApplyReplaceAsync(commanderDir, filesFromZip, ct);

        return ApplyMergeRaw(commanderDir, filesFromZip, dup);
    }

    private static (FrontierJournalExportManifestDto? Manifest, Dictionary<string, byte[]> Files) ReadAndValidateZip(Stream zipStream)
    {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            var leaf = Path.GetFileName(name);
            if (string.IsNullOrEmpty(leaf) || leaf.StartsWith('.') || name.Contains("..", StringComparison.Ordinal))
                continue;
            if (entry.Length == 0 && leaf != ManifestFileName) continue;

            using var es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            files[leaf] = ms.ToArray();
        }

        if (!files.TryGetValue(ManifestFileName, out var manifestBytes))
            throw new InvalidOperationException("manifest manquant");

        var manifest = JsonSerializer.Deserialize<FrontierJournalExportManifestDto>(Encoding.UTF8.GetString(manifestBytes), JsonRead);
        return (manifest, files);
    }

    private async Task<FrontierJournalImportResultDto> ApplyReplaceAsync(
        string commanderDir,
        Dictionary<string, byte[]> filesFromZip,
        CancellationToken ct)
    {
        foreach (var name in JournalDataFileNames)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(commanderDir, name);
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "[FrontierJournal] Import replace : suppression {File}", name);
                }
            }
        }

        foreach (var kv in filesFromZip)
        {
            if (string.Equals(kv.Key, ManifestFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!JournalDataFileNames.Contains(kv.Key))
                continue;

            var dest = Path.Combine(commanderDir, kv.Key);
            await File.WriteAllBytesAsync(dest, kv.Value, ct);
        }

        var rawCount = _backfill.LoadRaw(commanderDir).Count;
        _log.LogInformation("[FrontierJournal] Import replace terminé, jours bruts ~ {Count}", rawCount);
        return new FrontierJournalImportResultDto
        {
            Success = true,
            Strategy = "replace",
            Message = "Journal restauré depuis la sauvegarde (commande remplacer).",
            RawDayCount = rawCount,
        };
    }

    private FrontierJournalImportResultDto ApplyMergeRaw(string commanderDir, Dictionary<string, byte[]> filesFromZip, string duplicatePolicy)
    {
        if (!filesFromZip.TryGetValue(FrontierJournalBackfillService.RawFileName, out var rawBytes))
        {
            return new FrontierJournalImportResultDto
            {
                Success = false,
                Message = "Fusion impossible : la sauvegarde ne contient pas frontier-journal-raw.json.",
            };
        }

        Dictionary<string, FrontierJournalRawEntry>? importRaw;
        try
        {
            importRaw = JsonSerializer.Deserialize<Dictionary<string, FrontierJournalRawEntry>>(Encoding.UTF8.GetString(rawBytes), JsonRead);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[FrontierJournal] Fusion : JSON raw invalide");
            return new FrontierJournalImportResultDto
            {
                Success = false,
                Message = "fichier raw JSON invalide dans la sauvegarde.",
            };
        }

        if (importRaw == null)
        {
            return new FrontierJournalImportResultDto
            {
                Success = false,
                Message = "Contenu raw vide ou illisible.",
            };
        }

        var local = _backfill.LoadRaw(commanderDir);
        var added = 0;
        var overwritten = 0;
        var skipped = 0;

        foreach (var (date, imp) in importRaw)
        {
            if (string.IsNullOrWhiteSpace(date)) continue;
            if (!local.TryGetValue(date, out var existing))
            {
                local[date] = imp;
                added++;
                continue;
            }

            if (duplicatePolicy == "import")
            {
                local[date] = imp;
                overwritten++;
            }
            else
                skipped++;
        }

        _backfill.SaveRaw(commanderDir, local);
        _log.LogInformation(
            "[FrontierJournal] Import fusion raw : +{Added} jours, écrasés={Overwrite}, conflits conservés locaux={Skip}",
            added,
            overwritten,
            skipped);

        return new FrontierJournalImportResultDto
        {
            Success = true,
            Strategy = "merge",
            Message =
                $"Fusion du brut : {added} jour(s) ajouté(s), {overwritten} remplacé(s), {skipped} conflit(s) laissés localement. Progression / agrégats dérivés inchangés — lancez une synchro du journal pour mettre à jour la carte si besoin.",
            RawDayCount = local.Count,
            MergeAddedDays = added,
            MergeOverwrittenDays = overwritten,
            MergeSkippedDuplicateDays = skipped,
        };
    }

    private static string SanitizeFileSegment(string name)
    {
        var arr = name.Trim()
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '-' : c)
            .ToArray();
        var s = new string(arr);
        return string.IsNullOrWhiteSpace(s) ? "cmdr" : s[..Math.Min(s.Length, 48)];
    }

    /// <summary>Même esprit que le match Inara : CMDR, trim, 0↔O.</summary>
    private static bool CommanderNamesMatch(string? exported, string? connected)
    {
        var n1 = NormalizeCmdrName(exported);
        var n2 = NormalizeCmdrName(connected);
        return string.Equals(n1, n2, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCmdrName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.StartsWith("CMDR ", StringComparison.OrdinalIgnoreCase))
            n = n["CMDR ".Length..].Trim();
        return n.Replace('0', 'O');
    }
}

/// <summary>Métadonnées stockées dans le ZIP (racine).</summary>
public sealed class FrontierJournalExportManifestDto
{
    public string Format { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string CommanderName { get; set; } = "";
    public string FrontierCustomerId { get; set; } = "";
    public DateTime ExportedAt { get; set; }
    public string AppVersion { get; set; } = "";
    public IReadOnlyList<string> Files { get; set; } = Array.Empty<string>();
}

public sealed class FrontierJournalImportResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string Strategy { get; set; } = "";
    public int RawDayCount { get; set; }
    public int MergeAddedDays { get; set; }
    public int MergeOverwrittenDays { get; set; }
    public int MergeSkippedDuplicateDays { get; set; }
}
