using System.Text.Json;
using System.Text.RegularExpressions;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Data;

/// <summary>Charge les systèmes de la guild depuis guild-systems.seed.json. Insertion idempotente.</summary>
public class GuildSystemsSeedLoader
{
    private readonly GuildDashboardDbContext _db;
    private readonly ILogger<GuildSystemsSeedLoader> _log;
    private readonly IHostEnvironment _env;

    private static readonly Regex SpecialCharsRegex = new(@"[^\p{L}\p{N}\s\.\-\']", RegexOptions.Compiled);

    public GuildSystemsSeedLoader(GuildDashboardDbContext db, ILogger<GuildSystemsSeedLoader> log, IHostEnvironment env)
    {
        _db = db;
        _log = log;
        _env = env;
    }

    /// <summary>Charge et insère les systèmes du seed. Idempotent : ignore les systèmes déjà présents (GuildId + Name).</summary>
    public async Task<GuildSystemsSeedResult> LoadAsync(int guildId = 1, CancellationToken ct = default)
    {
        var baseDir = _env.ContentRootPath ?? AppContext.BaseDirectory ?? ".";
        var path = Path.Combine(baseDir, "Data", "guild-systems.seed.json");
        if (!File.Exists(path))
        {
            _log.LogWarning("[GuildSystemsSeed] Fichier absent: {Path} (baseDir={BaseDir})", path, baseDir);
            return new GuildSystemsSeedResult(0, 0, "Fichier seed absent", 0, 0, []);
        }

        var json = await File.ReadAllTextAsync(path, ct);
        List<ParsedSeedSystem> parsed;
        try
        {
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("systems", out var sysProp) || sysProp.ValueKind != JsonValueKind.Array)
                {
                    _log.LogWarning("[GuildSystemsSeed] Propriété 'systems' absente ou invalide");
                    return new GuildSystemsSeedResult(0, 0, "Propriété systems absente ou invalide", 0, 0, []);
                }
                parsed = sysProp.EnumerateArray()
                    .Select(e => ParseToSeedSystem(e, _log))
                    .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                    .Select(s => s with { Name = SystemNameNormalizer.Normalize(s.Name) })
                    .ToList();
            }
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "[GuildSystemsSeed] JSON invalide");
            return new GuildSystemsSeedResult(0, 0, "JSON invalide: " + ex.Message, 0, 0, []);
        }

        if (parsed.Count == 0)
        {
            _log.LogWarning("[GuildSystemsSeed] Tableau systems vide");
            return new GuildSystemsSeedResult(0, 0, "Aucun système", 0, 0, []);
        }

        var totalSource = parsed.Count;
        var existingNames = await _db.GuildSystems
            .Where(s => s.GuildId == guildId)
            .Select(s => s.Name)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(
            existingNames.Select(SystemNameNormalizer.Normalize).Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);

        var inserted = 0;
        var ignored = 0;

        foreach (var p in parsed)
        {
            var normalizedName = SystemNameNormalizer.Normalize(p.Name);
            if (string.IsNullOrWhiteSpace(normalizedName) || existingSet.Contains(normalizedName))
            {
                ignored++;
                continue;
            }

            var system = ToGuildSystem(guildId, p);

            try
            {
                _db.GuildSystems.Add(system);
                await _db.SaveChangesAsync(ct);
                existingSet.Add(normalizedName);
                inserted++;
            }
            catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
            {
                ignored++;
                _log.LogDebug("[GuildSystemsSeed] Système déjà existant (concurrence): {Name}", p.Name);
            }
        }

        await DeduplicateByNameAsync(guildId, parsed.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase), ct);

        var totalFinal = await _db.GuildSystems.CountAsync(s => s.GuildId == guildId, ct);
        var sourceNames = new HashSet<string>(parsed.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var finalNames = await _db.GuildSystems
            .Where(s => s.GuildId == guildId)
            .Select(s => s.Name)
            .ToListAsync(ct);
        var finalSet = new HashSet<string>(finalNames, StringComparer.OrdinalIgnoreCase);
        var missing = sourceNames.Where(n => !finalSet.Contains(n)).OrderBy(n => n).ToList();

        _log.LogInformation(
            "[GuildSystemsSeed] RÉSULTAT: totalSource={TotalSource} inserted={Inserted} ignored={Ignored} totalFinal={TotalFinal}",
            totalSource, inserted, ignored, totalFinal);

        if (missing.Count > 0)
            _log.LogWarning("[GuildSystemsSeed] {Count} système(s) source absent(s) en base: {Names}", missing.Count, string.Join(", ", missing));

        return new GuildSystemsSeedResult(inserted, ignored, null, totalSource, totalFinal, missing);
    }

    private static string CleanName(JsonElement je)
    {
        if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined) return string.Empty;
        var value = je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = SpecialCharsRegex.Replace(value.Trim(), "");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }


    private static long ParseLong(JsonElement je)
    {
        if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined) return 0;
        if (je.ValueKind == JsonValueKind.Number) return je.GetInt64();
        var s = je.GetString();
        if (string.IsNullOrWhiteSpace(s)) return 0;
        return long.TryParse(Regex.Replace(s, @"[^\d]", ""), out var r) ? r : 0;
    }

    private static int ParseInt(JsonElement je)
    {
        if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined) return 0;
        if (je.ValueKind == JsonValueKind.Number) return je.GetInt32();
        var s = je.GetString();
        if (string.IsNullOrWhiteSpace(s)) return 0;
        return int.TryParse(Regex.Replace(s, @"[^\d]", ""), out var r) ? r : 0;
    }

    private static string? ParseString(JsonElement je)
    {
        if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined) return null;
        var s = je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static bool ParseBool(JsonElement je)
    {
        return je.ValueKind == JsonValueKind.True;
    }

    private static ParsedSeedSystem ParseToSeedSystem(JsonElement raw, ILogger? log)
    {
        var name = CleanName(GetProp(raw, "name"));
        return new ParsedSeedSystem(
            Name: name,
            Government: ParseString(GetProp(raw, "government")),
            Allegiance: ParseString(GetProp(raw, "allegiance")),
            Power: ParseString(GetProp(raw, "power")),
            Population: ParseLong(GetProp(raw, "population")),
            FactionCount: ParseInt(GetProp(raw, "factionCount")),
            StationCount: ParseInt(GetProp(raw, "stationCount")),
            InfluencePercent: InfluenceParse.ParseStrict(GetProp(raw, "influencePercent"), log, "seed"),
            LastUpdatedText: ParseString(GetProp(raw, "lastUpdatedText")),
            Category: ParseString(GetProp(raw, "category")),
            IsClean: ParseBool(GetProp(raw, "isClean"))
        );
    }

    private static GuildSystem ToGuildSystem(int guildId, ParsedSeedSystem p)
    {
        return new GuildSystem
        {
            GuildId = guildId,
            Name = p.Name,
            Category = p.Category ?? "Guild",
            InfluencePercent = p.InfluencePercent,
            IsClean = p.IsClean,
            Government = p.Government,
            Allegiance = p.Allegiance,
            Power = p.Power,
            Population = p.Population,
            FactionCount = p.FactionCount,
            StationCount = p.StationCount,
            LastUpdatedText = p.LastUpdatedText,
        };
    }

    private static JsonElement GetProp(JsonElement obj, string propName)
    {
        return obj.TryGetProperty(propName, out var p) ? p : default;
    }

    private record ParsedSeedSystem(
        string Name,
        string? Government,
        string? Allegiance,
        string? Power,
        long Population,
        int FactionCount,
        int StationCount,
        decimal InfluencePercent,
        string? LastUpdatedText,
        string? Category,
        bool IsClean);

    /// <summary>Supprime les doublons (même nom normalisé). Garde la forme préférée (seed ou canonique).</summary>
    private async Task DeduplicateByNameAsync(int guildId, HashSet<string> preferredNames, CancellationToken ct)
    {
        var all = await _db.GuildSystems.Where(s => s.GuildId == guildId).ToListAsync(ct);
        var byNorm = all
            .Select(g => (gs: g, norm: SystemNameNormalizer.Normalize(g.Name)))
            .Where(x => !string.IsNullOrWhiteSpace(x.norm))
            .GroupBy(x => x.norm, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in byNorm)
        {
            var list = group.OrderBy(x => x.gs.Name).ToList();
            var toKeep = list.FirstOrDefault(x => preferredNames.Contains(SystemNameNormalizer.Normalize(x.gs.Name))).gs
                         ?? list.First().gs;
            var toRemove = list.Where(x => x.gs.Id != toKeep.Id).Select(x => x.gs).ToList();

            foreach (var gs in toRemove)
            {
                await _db.ControlledSystems
                    .Where(c => c.GuildId == guildId && c.Name == gs.Name)
                    .ExecuteDeleteAsync(ct);
                _db.GuildSystems.Remove(gs);
                _log.LogInformation("[GuildSystemsSeed] Suppression doublon: {Name} (gardé: {Kept})", gs.Name, toKeep.Name);
            }
        }

        if (byNorm.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex) =>
        ex.InnerException?.Message?.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message?.Contains("unique", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Vérifie que les systèmes seed sont bien présents en base. Ne modifie rien.</summary>
    public async Task<GuildSystemsSeedVerification> VerifyAsync(int guildId = 1, CancellationToken ct = default)
    {
        var baseDir = _env.ContentRootPath ?? AppContext.BaseDirectory ?? ".";
        var path = Path.Combine(baseDir, "Data", "guild-systems.seed.json");
        if (!File.Exists(path))
            return new GuildSystemsSeedVerification(0, 0, [], "Fichier seed absent");

        List<string> sourceNames;
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("systems", out var sysProp) || sysProp.ValueKind != JsonValueKind.Array)
                return new GuildSystemsSeedVerification(0, 0, [], "Propriété systems absente");
            sourceNames = sysProp.EnumerateArray()
                .Select(e => SystemNameNormalizer.Normalize(CleanName(GetProp(e, "name"))))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            return new GuildSystemsSeedVerification(0, 0, [], "Erreur lecture seed: " + ex.Message);
        }

        var dbNames = await _db.GuildSystems
            .Where(s => s.GuildId == guildId)
            .Select(s => s.Name)
            .ToListAsync(ct);
        var dbSet = new HashSet<string>(
            dbNames.Select(SystemNameNormalizer.Normalize).Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);
        var missing = sourceNames.Where(n => !dbSet.Contains(n)).OrderBy(n => n).ToList();

        return new GuildSystemsSeedVerification(sourceNames.Count, dbNames.Count, missing, null);
    }
}

public record GuildSystemsSeedVerification(int TotalExpected, int TotalInDb, IReadOnlyList<string> MissingNames, string? Error);

public record GuildSystemsSeedResult(
    int Inserted,
    int Ignored,
    string? Error,
    int TotalSource = 0,
    int TotalFinal = 0,
    IReadOnlyList<string>? MissingNames = null);
