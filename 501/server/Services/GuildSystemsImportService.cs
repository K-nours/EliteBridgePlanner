using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GuildDashboard.Server.Data;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Import idempotent de systèmes depuis JSON (userscript Inara, seed, etc.). Upsert par (GuildId, Name).</summary>
public class GuildSystemsImportService
{
    private readonly GuildDashboardDbContext _db;
    private readonly ILogger<GuildSystemsImportService> _log;

    private static readonly string[] AuditSystemNames = ["Mayang", "HIP 4332", "Sabines", "Nanapan"];

    private static readonly Regex SpecialCharsRegex = new(@"[^\p{L}\p{N}\s\.\-']", RegexOptions.Compiled);

    public GuildSystemsImportService(GuildDashboardDbContext db, ILogger<GuildSystemsImportService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>Importe les systèmes. Mise à jour si existant (par nom), insertion si nouveau. purgeAbsent=true supprime les systèmes absents du payload.</summary>
    public async Task<GuildSystemsImportResult> ImportAsync(int guildId, GuildSystemsImportPayload payload, bool purgeAbsent = true, CancellationToken ct = default)
    {
        var totalReceived = payload?.Systems?.Count ?? 0;
        _log.LogInformation("[GuildSystemsImport] DÉBUT guildId={GuildId} totalReceived={TotalReceived} payloadNull={PayloadNull} originSystemName={Origin}",
            guildId, totalReceived, payload == null, payload?.OriginSystemName ?? "(null)");

        if (totalReceived == 0)
            return new GuildSystemsImportResult(0, 0, 0, 0, 0, "Aucun système dans le payload");

        var firstRaw = payload!.Systems!.FirstOrDefault();
        if (firstRaw != null)
        {
            _log.LogInformation("[GuildSystemsImport] Premier système: Name={Name} InfluencePercent={Inf} Category={Cat}",
                firstRaw.Name ?? "(null)", firstRaw.InfluencePercent?.ToString("0.##") ?? "null", firstRaw.Category ?? "(null)");
        }
        var samplePayloadCategories = payload!.Systems!.Take(5).Select(s => s.Category ?? "null").ToList();
        _log.LogInformation("[GuildSystemsImport] Échantillon payload categories (5 premiers): [{Cats}]", string.Join(", ", samplePayloadCategories));

        // Nettoyage des influences corrompues (> 100) avant la prochaine resync
        await CleanupCorruptedInfluenceAsync(guildId, ct);

        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var deleted = 0;

        var incomingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in payload!.Systems!)
        {
            var n = SystemNameNormalizer.Normalize(CleanName(raw.Name));
            if (!string.IsNullOrWhiteSpace(n)) incomingNames.Add(n);
        }

        var guildSystems = await _db.GuildSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync(ct);
        var controlledSystems = await _db.ControlledSystems
            .Where(c => c.GuildId == guildId)
            .ToListAsync(ct);

        _log.LogInformation("[GuildSystemsImport] État base: guildSystems={GsCount} controlledSystems={CsCount} (reset/vide={BaseVide})",
            guildSystems.Count, controlledSystems.Count, guildSystems.Count == 0 && controlledSystems.Count == 0);

        // Purge des systèmes absents du payload (remise à plat propre)
        if (purgeAbsent && incomingNames.Count > 0)
        {
            var toRemove = guildSystems
                .Where(s => !incomingNames.Contains(SystemNameNormalizer.Normalize(s.Name)))
                .ToList();
            foreach (var gs in toRemove)
            {
                await _db.ControlledSystems
                    .Where(c => c.GuildId == guildId && c.Name == gs.Name)
                    .ExecuteDeleteAsync(ct);
                _db.GuildSystems.Remove(gs);
                deleted++;
                _log.LogInformation("[GuildSystemsImport] Purge système obsolète: {Name}", gs.Name);
            }
            guildSystems = guildSystems.Except(toRemove).ToList();
        }

        var firstImportedName = (string?)null;
        var firstLowName = (string?)null;
        var firstHighName = (string?)null;
        foreach (var raw in payload!.Systems!)
        {
            var inf = raw.InfluencePercent ?? 0;
            if (inf >= 0 && inf < 4 && firstLowName == null)
                firstLowName = SystemNameNormalizer.Normalize(CleanName(raw.Name));
            if (inf >= 60 && firstHighName == null)
                firstHighName = SystemNameNormalizer.Normalize(CleanName(raw.Name));
        }
        var auditNames = new HashSet<string>(AuditSystemNames.Select(SystemNameNormalizer.Normalize).Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(firstLowName)) auditNames.Add(firstLowName);
        if (!string.IsNullOrWhiteSpace(firstHighName)) auditNames.Add(firstHighName);

        foreach (var raw in payload!.Systems!)
        {
            var name = SystemNameNormalizer.Normalize(CleanName(raw.Name));
            if (string.IsNullOrWhiteSpace(name))
            {
                skipped++;
                continue;
            }
            if (firstImportedName == null)
                firstImportedName = name;

            var existing = guildSystems.FirstOrDefault(s =>
                string.Equals(SystemNameNormalizer.Normalize(s.Name), name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                var oldName = existing.Name;
                ApplyUpdate(existing, raw, name);
                _db.GuildSystems.Update(existing);
                if (!string.Equals(oldName, name, StringComparison.Ordinal))
                {
                    await _db.ControlledSystems
                        .Where(c => c.GuildId == guildId && c.Name == oldName)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, name), ct);
                }
                // Synchroniser ControlledSystem avec les données Inara (influence, catégorie après préservation Origine)
                var influenceVal = raw.InfluencePercent.HasValue ? InfluenceParse.Sanitize(raw.InfluencePercent.Value) : (decimal?)null;
                SyncControlledSystemFromImport(controlledSystems, name, influenceVal, existing.Category);
                LogSystemAudit(auditNames, existing.Name, raw.InfluencePercent, influenceVal, existing.InfluencePercent, existing.Category, controlledSystems);
                LogCategoryAndInfluenceDiagnostic(auditNames, name, raw.Category, existing.Category, raw.InfluencePercent, influenceVal, existing.InfluencePercent, controlledSystems);
                updated++;
            }
            else
            {
                var entity = ToGuildSystem(guildId, raw, name);
                _db.GuildSystems.Add(entity);
                guildSystems.Add(entity);
                var influenceVal = raw.InfluencePercent.HasValue ? InfluenceParse.Sanitize(raw.InfluencePercent.Value) : 0m;
                var cs = new ControlledSystem
                {
                    GuildId = guildId,
                    Name = name,
                    InfluencePercent = influenceVal,
                    Category = entity.Category,
                    IsClean = entity.IsClean,
                    IsFromSeed = false,
                    IsControlled = false,
                    IsHeadquarter = false,
                    IsThreatened = false,
                    IsExpansionCandidate = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                _db.ControlledSystems.Add(cs);
                controlledSystems.Add(cs);
                _log.LogDebug("[GuildSystemsImport] ControlledSystem créé: {Name} influence={Inf} category={Cat}", name, influenceVal, entity.Category);
                LogSystemAudit(auditNames, name, raw.InfluencePercent, influenceVal, entity.InfluencePercent, entity.Category, controlledSystems);
                LogCategoryAndInfluenceDiagnostic(auditNames, name, raw.Category, entity.Category, raw.InfluencePercent, influenceVal, entity.InfluencePercent, controlledSystems);
                inserted++;
            }
        }

        if (inserted > 0 || updated > 0 || deleted > 0)
        {
            var originFromPayload = !string.IsNullOrWhiteSpace(payload.OriginSystemName)
                ? SystemNameNormalizer.Normalize(CleanName(payload.OriginSystemName))
                : null;
            await RebuildCategoriesAsync(guildId, originFromPayload, firstImportedName, guildSystems, controlledSystems, ct);
            LogPostRebuildDiagnostic(auditNames, guildSystems, controlledSystems);
            await _db.SaveChangesAsync(ct);
            await _db.Guilds
                .Where(g => g.Id == guildId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.LastSystemsImportAt, DateTime.UtcNow), ct);
        }

        var guildName = await _db.Guilds
            .AsNoTracking()
            .Where(g => g.Id == guildId)
            .Select(g => g.DisplayName ?? g.Name)
            .FirstOrDefaultAsync(ct) ?? $"Guild {guildId}";

        _log.LogInformation(
            "[GuildSystemsImport] reçus={TotalReceived} insérés={Inserted} mis à jour={Updated} purgés={Deleted} ignorés={Skipped} | cible={GuildName}",
            totalReceived, inserted, updated, deleted, skipped, guildName);

        return new GuildSystemsImportResult(totalReceived, inserted, updated, skipped, deleted, null);
    }

    /// <summary>Met à jour ControlledSystem avec InfluencePercent et Category depuis l'import Inara. Match insensible à la casse.</summary>
    private void SyncControlledSystemFromImport(List<ControlledSystem> controlledSystems, string name, decimal? influencePercent, string? category)
    {
        var normalized = SystemNameNormalizer.Normalize(name);
        foreach (var cs in controlledSystems.Where(c => string.Equals(SystemNameNormalizer.Normalize(c.Name), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            if (influencePercent.HasValue)
                cs.InfluencePercent = influencePercent.Value;
            if (!string.IsNullOrWhiteSpace(category))
                cs.Category = category;
            cs.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>Reconstruit les catégories après import : Origine (originSystemName du payload = source de vérité, sinon fallback premier système).</summary>
    /// <remarks>Utilise la liste en mémoire car SaveChanges n'a pas encore été appelé — les nouvelles entités ne sont pas en DB.</remarks>
    private Task RebuildCategoriesAsync(int guildId, string? originFromPayload, string? firstImportedName, List<GuildSystem> guildSystems, List<ControlledSystem> controlledSystems, CancellationToken ct)
    {
        var hasOrigin = guildSystems.Any(s => string.Equals(s.Category, "Origine", StringComparison.OrdinalIgnoreCase));
        _log.LogInformation("[GuildSystemsImport] RebuildCategories: count={Count} hasOrigin={HasOrigin} originFromPayload={Origin} fallback={Fallback}",
            guildSystems.Count, hasOrigin, originFromPayload ?? "(null)", firstImportedName ?? "(null)");

        var originToApply = !string.IsNullOrWhiteSpace(originFromPayload)
            ? originFromPayload
            : (!hasOrigin ? firstImportedName : null);

        if (!string.IsNullOrWhiteSpace(originToApply))
        {
            foreach (var gs in guildSystems.Where(s => string.Equals(s.Category, "Origine", StringComparison.OrdinalIgnoreCase)))
            {
                gs.Category = "Guild";
                foreach (var cs in controlledSystems.Where(c => string.Equals(SystemNameNormalizer.Normalize(c.Name), SystemNameNormalizer.Normalize(gs.Name), StringComparison.OrdinalIgnoreCase)))
                    cs.Category = "Guild";
            }
            var target = guildSystems.FirstOrDefault(s =>
                string.Equals(SystemNameNormalizer.Normalize(s.Name), originToApply, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                target.Category = "Origine";
                foreach (var cs in controlledSystems.Where(c =>
                    string.Equals(SystemNameNormalizer.Normalize(c.Name), originToApply, StringComparison.OrdinalIgnoreCase)))
                {
                    cs.Category = "Origine";
                }
                _log.LogInformation("[GuildSystemsImport] Origine assignée: {Name}", target.Name);
            }
            else
                _log.LogWarning("[GuildSystemsImport] originSystemName={Origin} non trouvé dans guildSystems", originToApply ?? "(null)");
        }
        return Task.CompletedTask;
    }

    private void LogSystemAudit(HashSet<string> auditNames, string name, decimal? rawInara, decimal? parsed, decimal guildVal, string? category, List<ControlledSystem> controlledSystems)
    {
        var norm = SystemNameNormalizer.Normalize(name);
        if (string.IsNullOrWhiteSpace(norm) || !auditNames.Contains(norm)) return;
        var cs = controlledSystems.FirstOrDefault(c => string.Equals(SystemNameNormalizer.Normalize(c.Name), SystemNameNormalizer.Normalize(name), StringComparison.OrdinalIgnoreCase));
        var cVal = cs?.InfluencePercent;
        _log.LogInformation(
            "[Systems] {Name} — rawInara={Raw} parsed={Parsed} guild={Guild} controlled={Controlled} category={Category}",
            name, rawInara?.ToString("0.##") ?? "null", parsed?.ToString("0.##") ?? "null",
            guildVal.ToString("0.##"), (cVal ?? 0).ToString("0.##"), category ?? "null");
    }

    /// <summary>Diagnostic catégorisation et influence pour les systèmes d'audit (Mayang, HIP 4332, Sabines, Nanapan). Avant RebuildCategories.</summary>
    private void LogCategoryAndInfluenceDiagnostic(HashSet<string> auditNames, string name, string? payloadCategory, string guildSystemCategory, decimal? rawInara, decimal? parsedInfluence, decimal guildSystemInfluence, List<ControlledSystem> controlledSystems)
    {
        var norm = SystemNameNormalizer.Normalize(name);
        if (string.IsNullOrWhiteSpace(norm) || !auditNames.Contains(norm)) return;
        var cs = controlledSystems.FirstOrDefault(c => string.Equals(SystemNameNormalizer.Normalize(c.Name), norm, StringComparison.OrdinalIgnoreCase));
        var isOrigin = string.Equals(guildSystemCategory, "Origine", StringComparison.OrdinalIgnoreCase);
        var isHq = cs?.IsHeadquarter == true;
        var isCritical = cs?.IsThreatened == true || cs?.IsExpansionCandidate == true;
        var finalDisplay = isOrigin ? "origin" : (isHq ? "headquarter" : (isCritical ? "critical" : "others"));
        var controlledVal = cs?.InfluencePercent;
        _log.LogWarning(
            "[DIAG-PRE] {Name} | payloadCategory={PayloadCat} guildSystemCategory={GsCat} finalDisplay={Final} | isHq={Hq} isThreatened={Thr} isExpansion={Exp} | rawInara={Raw} parsed={Parsed} guild={Gs} controlled={Cs}",
            name, payloadCategory ?? "null", guildSystemCategory ?? "null", finalDisplay,
            isHq, cs?.IsThreatened ?? false, cs?.IsExpansionCandidate ?? false,
            rawInara?.ToString("0.##") ?? "null", parsedInfluence?.ToString("0.##") ?? "null", guildSystemInfluence.ToString("0.##"), (controlledVal ?? 0).ToString("0.##"));
    }

    /// <summary>Diagnostic APRÈS RebuildCategories — état final pour audit (Mayang, HIP 4332, Sabines, Nanapan).</summary>
    private void LogPostRebuildDiagnostic(HashSet<string> _1, List<GuildSystem> guildSystems, List<ControlledSystem> controlledSystems)
    {
        foreach (var name in AuditSystemNames)
        {
            var norm = SystemNameNormalizer.Normalize(name);
            if (string.IsNullOrWhiteSpace(norm)) continue;
            var gs = guildSystems.FirstOrDefault(s => string.Equals(SystemNameNormalizer.Normalize(s.Name), norm, StringComparison.OrdinalIgnoreCase));
            var cs = gs != null ? controlledSystems.FirstOrDefault(c => string.Equals(SystemNameNormalizer.Normalize(c.Name), SystemNameNormalizer.Normalize(gs.Name), StringComparison.OrdinalIgnoreCase)) : null;
            if (gs == null) continue;
            var isOrigin = string.Equals(gs.Category, "Origine", StringComparison.OrdinalIgnoreCase);
            var isHq = cs?.IsHeadquarter == true;
            var isCritical = cs?.IsThreatened == true || cs?.IsExpansionCandidate == true;
            var finalDisplay = isOrigin ? "origin" : (isHq ? "headquarter" : (isCritical ? "critical" : "others"));
            var dtoInfluence = gs.InfluencePercent; // source de vérité DTO = GuildSystem
            _log.LogWarning(
                "[DIAG-POST] {Name} | guildSystemCategory={GsCat} finalDisplayCategory={Final} dtoInfluence={Dto} | guildInfluence={Gs} controlledInfluence={Cs}",
                gs.Name, gs.Category ?? "null", finalDisplay, dtoInfluence.ToString("0.##"), gs.InfluencePercent.ToString("0.##"), (cs?.InfluencePercent ?? 0).ToString("0.##"));
        }
    }

    private void ApplyUpdate(GuildSystem entity, GuildSystemImportItem raw, string normalizedName)
    {
        entity.Name = normalizedName;
        if (raw.Government != null) entity.Government = raw.Government;
        if (raw.Allegiance != null) entity.Allegiance = raw.Allegiance;
        if (raw.Power != null) entity.Power = string.IsNullOrWhiteSpace(raw.Power) ? null : raw.Power;
        entity.Population = raw.Population ?? entity.Population;
        entity.FactionCount = raw.FactionCount ?? entity.FactionCount;
        entity.StationCount = raw.StationCount ?? entity.StationCount;
        if (raw.InfluencePercent.HasValue)
            entity.InfluencePercent = InfluenceParse.Sanitize(raw.InfluencePercent.Value);
        if (raw.LastUpdatedText != null) entity.LastUpdatedText = raw.LastUpdatedText;
        if (raw.Category != null)
        {
            // Préserver "Origine" : le userscript Inara envoie toujours "Guild", ne pas écraser si c'est l'origine
            if (!(string.Equals(raw.Category, "Guild", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(entity.Category, "Origine", StringComparison.OrdinalIgnoreCase)))
                entity.Category = raw.Category;
        }
        entity.IsClean = raw.IsClean ?? entity.IsClean;
    }

    private static GuildSystem ToGuildSystem(int guildId, GuildSystemImportItem raw, string normalizedName)
    {
        return new GuildSystem
        {
            GuildId = guildId,
            Name = normalizedName,
            Government = raw.Government,
            Allegiance = raw.Allegiance,
            Power = string.IsNullOrWhiteSpace(raw.Power) ? null : raw.Power,
            Population = raw.Population ?? 0,
            FactionCount = raw.FactionCount ?? 0,
            StationCount = raw.StationCount ?? 0,
            InfluencePercent = InfluenceParse.Sanitize(raw.InfluencePercent ?? 0),
            LastUpdatedText = raw.LastUpdatedText,
            Category = raw.Category ?? "Guild",
            IsClean = raw.IsClean ?? false,
        };
    }

    /// <summary>Remet à 0 les InfluencePercent corrompues (> 100) dans GuildSystems et ControlledSystems.</summary>
    private async Task CleanupCorruptedInfluenceAsync(int guildId, CancellationToken ct)
    {
        var corruptGuild = await _db.GuildSystems
            .Where(s => s.GuildId == guildId && s.InfluencePercent > 100)
            .Select(s => new { s.Id, s.Name, s.InfluencePercent })
            .ToListAsync(ct);
        var corruptControlled = await _db.ControlledSystems
            .Where(c => c.GuildId == guildId && c.InfluencePercent > 100)
            .Select(c => new { c.Id, c.Name, c.InfluencePercent })
            .ToListAsync(ct);

        var totalFixed = 0;
        var names = new List<string>();

        foreach (var g in corruptGuild)
        {
            await _db.GuildSystems.Where(s => s.Id == g.Id).ExecuteUpdateAsync(
                s => s.SetProperty(x => x.InfluencePercent, 0m), ct);
            totalFixed++;
            if (!names.Contains(g.Name)) names.Add(g.Name);
        }
        foreach (var c in corruptControlled)
        {
            await _db.ControlledSystems.Where(x => x.Id == c.Id).ExecuteUpdateAsync(
                s => s.SetProperty(x => x.InfluencePercent, 0m), ct);
            totalFixed++;
            if (!names.Contains(c.Name)) names.Add(c.Name);
        }

        if (totalFixed > 0)
        {
            _log.LogInformation("[GuildSystemsImport] Nettoyage influences corrompues (>100%): {Count} ligne(s) corrigée(s), systèmes affectés: [{Names}]",
                totalFixed, string.Join(", ", names.OrderBy(n => n)));
        }
    }

    private static string CleanName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = SpecialCharsRegex.Replace(value.Trim(), "");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    /// <summary>Parse le JSON brut en payload. Compatible avec le format du userscript et du seed.</summary>
    public static GuildSystemsImportPayload? ParsePayload(string json)
    {
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<GuildSystemsImportPayload>(json, opts);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Payload d'import (format userscript Inara / seed).</summary>
public class GuildSystemsImportPayload
{
    /// <summary>Nom du système Origine lu depuis le header Inara (bloc infos faction). Source de vérité pour la catégorie Origine.</summary>
    [JsonPropertyName("originSystemName")]
    public string? OriginSystemName { get; set; }
    [JsonPropertyName("systems")]
    public List<GuildSystemImportItem> Systems { get; set; } = new();
}

/// <summary>Un système à importer. Tous les champs optionnels sauf Name.</summary>
public class GuildSystemImportItem
{
    public string Name { get; set; } = string.Empty;
    public string? Government { get; set; }
    public string? Allegiance { get; set; }
    public string? Power { get; set; }
    public long? Population { get; set; }
    public int? FactionCount { get; set; }
    public int? StationCount { get; set; }
    public decimal? InfluencePercent { get; set; }
    public string? LastUpdatedText { get; set; }
    public string? Category { get; set; }
    public bool? IsClean { get; set; }
}

public record GuildSystemsImportResult(int TotalReceived, int Inserted, int Updated, int Skipped, int Deleted, string? Error);
