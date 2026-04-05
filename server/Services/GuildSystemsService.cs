// Feature archived – no reliable external data source for Faction → Systems → Influence %.
// Conserver pour R&D futur. Voir docs/GUILD-SYSTEMS.md § Raison de clôture.

using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Service dédié au panneau Guild Systems.</summary>
/// <remarks>
/// DataSource : seed = données seedées/démo (DataSeeder). cached = données issues d'une sync BGS (EDSM, etc.).
/// Jamais "live" sans sync fraîche vérifiée.
/// </remarks>
public class GuildSystemsService
{
    private readonly GuildDashboardDbContext _db;
    private readonly InaraFactionService _inaraFaction;
    private readonly ILogger<GuildSystemsService> _log;

    public GuildSystemsService(GuildDashboardDbContext db, InaraFactionService inaraFaction, ILogger<GuildSystemsService> log)
    {
        _db = db;
        _inaraFaction = inaraFaction;
        _log = log;
    }

    public async Task<GuildSystemsResponseDto> GetSystemsAsync(int guildId = 1)
    {
        var guildExists = await _db.Guilds.AnyAsync(g => g.Id == guildId);
        if (!guildExists)
            return NewEmptyResponse();

        var guildSystems = await _db.GuildSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync();
        var controlled = await _db.ControlledSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync();
        var controlledByName = controlled.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var origin = new List<GuildSystemBgsDto>();
        var headquarter = new List<GuildSystemBgsDto>();
        var surveillance = new List<GuildSystemBgsDto>();
        var conflicts = new List<GuildSystemBgsDto>();
        var critical = new List<GuildSystemBgsDto>();
        var low = new List<GuildSystemBgsDto>();
        var healthy = new List<GuildSystemBgsDto>();
        var others = new List<GuildSystemBgsDto>();
        var anyFromSync = false;

        const decimal TacticalCritical = 5m;
        const decimal TacticalLow = 15m;
        const decimal TacticalHigh = 60m;

        foreach (var gs in guildSystems)
        {
            var cs = controlledByName.GetValueOrDefault(gs.Name);
            var dto = ToDto(gs, cs);
            var isOrigin = string.Equals(gs.Category, "Origine", StringComparison.OrdinalIgnoreCase);
            var isHq = cs?.IsHeadquarter == true;
            var isSurveillance = cs?.IsUnderSurveillance == true;
            var isConflict = IsConflictState(dto.State);
            var influence = dto.InfluencePercent;
            var isCritical = influence < TacticalCritical;
            var isLow = influence < TacticalLow && !isCritical;
            var isHealthy = influence >= TacticalHigh;

            if (cs != null && !cs.IsFromSeed)
                anyFromSync = true;

            // Surveillance et Conflits : additifs (n'excluent pas des autres catégories). Toutes les catégories peuvent se chevaucher.
            if (isOrigin)
                origin.Add(dto);
            if (isHq)
                headquarter.Add(dto);
            if (isSurveillance)
                surveillance.Add(dto);
            if (isConflict)
                conflicts.Add(dto);
            if (isCritical)
                critical.Add(dto);
            else if (isLow)
                low.Add(dto);
            else if (isHealthy)
                healthy.Add(dto);
            else
                others.Add(dto);
        }

        surveillance = surveillance.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();
        conflicts = conflicts.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();
        critical = critical.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();
        low = low.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();
        healthy = healthy.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();
        others = others.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();

        var dataSource = anyFromSync ? "cached" : "seed";
        var thresholds = new InfluenceThresholdsDto(
            InfluenceThresholds.Critical, InfluenceThresholds.Low, InfluenceThresholds.High);
        var tactical = new TacticalThresholdsDto(TacticalCritical, TacticalLow, TacticalHigh);
        var result = new GuildSystemsResponseDto(origin, headquarter, surveillance, conflicts, critical, low, healthy, others, dataSource, thresholds, tactical);
        LogDeltaDiagnostic(guildSystems, controlledByName);
        return result;
    }

    /// <summary>Retourne la distribution InfluenceDelta72h pour diagnostic (nonNull, roundedToZero, displayable).</summary>
    public async Task<(int NonNull, int RoundedToZero, int Displayable)> GetDeltaDistributionAsync(int guildId, CancellationToken ct = default)
    {
        var guildSystems = await _db.GuildSystems.Where(s => s.GuildId == guildId).ToListAsync(ct);
        var controlled = await _db.ControlledSystems.Where(c => c.GuildId == guildId).ToListAsync(ct);
        var controlledByName = controlled.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        int nonNull = 0, roundedToZero = 0, displayable = 0;
        foreach (var gs in guildSystems)
        {
            var cs = controlledByName.GetValueOrDefault(gs.Name);
            var dto = ToDto(gs, cs);
            var delta = dto.InfluenceDelta72h;
            if (delta == null) continue;
            nonNull++;
            var rounded = Math.Round((double)delta.Value, 2);
            if (Math.Abs(rounded) < 0.005)
                roundedToZero++;
            else
                displayable++;
        }
        return (nonNull, roundedToZero, displayable);
    }

    /// <summary>Diagnostic InfluenceDelta72h : distribution globale + échantillon DTO.</summary>
    private void LogDeltaDiagnostic(List<GuildSystem> guildSystems, Dictionary<string, ControlledSystem> controlledByName)
    {
        int nonNull = 0, roundedToZero = 0, displayable = 0;
        var sampleLogged = 0;
        foreach (var gs in guildSystems)
        {
            var cs = controlledByName.GetValueOrDefault(gs.Name);
            var dto = ToDto(gs, cs);
            var delta = dto.InfluenceDelta72h;
            if (delta == null) continue;
            nonNull++;
            var rounded = Math.Round((double)delta.Value, 2);
            if (Math.Abs(rounded) < 0.005)
                roundedToZero++;
            else
                displayable++;
            if (sampleLogged < 5)
            {
                _log.LogInformation("[GuildSystems][DIAG] DTO {Name} — InfluenceDelta72h brut={Raw} arrondi2d={Rounded}",
                    gs.Name, delta.Value.ToString("F6"), rounded.ToString("F2"));
                sampleLogged++;
            }
        }
        _log.LogWarning("[GuildSystems][DIAG] Distribution delta: nonNull={NonNull} roundedToZero={RoundedToZero} displayable={Displayable}",
            nonNull, roundedToZero, displayable);
    }

    private static bool IsConflictState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return false;
        var parts = state.Split(',', StringSplitOptions.TrimEntries);
        foreach (var s in parts)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            if (s.Equals("Conflit", StringComparison.OrdinalIgnoreCase)
                || s.Equals("War", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Civil War", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Civil Unrest", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Election", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Retribution", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static GuildSystemsResponseDto NewEmptyResponse()
    {
        var th = new InfluenceThresholdsDto(InfluenceThresholds.Critical, InfluenceThresholds.Low, InfluenceThresholds.High);
        var tactical = new TacticalThresholdsDto(5, 15, 60);
        return new GuildSystemsResponseDto([], [], [], [], [], [], [], [], "seed", th, tactical);
    }

    /// <summary>Audit ciblé : pour chaque système demandé, retourne valeurs brutes, parsées, stockées, DTO, catégorie et statut.</summary>
    public async Task<IReadOnlyList<GuildSystemAuditEntry>> GetAuditAsync(int guildId, IReadOnlyList<string> systemNames, CancellationToken ct = default)
    {
        var result = new List<GuildSystemAuditEntry>();
        if (systemNames.Count == 0) return result;

        var normalized = systemNames
            .Select(n => SystemNameNormalizer.Normalize(n?.Trim() ?? ""))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0) return result;

        var guild = await _db.Guilds.AsNoTracking().FirstOrDefaultAsync(g => g.Id == guildId, ct);
        var inaraByName = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (guild?.InaraFactionId is > 0)
        {
            var presence = await _inaraFaction.GetFactionPresenceAsync(guild.InaraFactionId.Value, ct);
            if (presence != null)
            {
                foreach (var p in presence)
                {
                    var n = SystemNameNormalizer.Normalize(p.SystemName ?? "");
                    if (!string.IsNullOrWhiteSpace(n))
                        inaraByName[n] = p.InfluencePercent;
                }
            }
        }

        var guildSystems = await _db.GuildSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync(ct);
        var controlled = await _db.ControlledSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync(ct);
        var controlledByName = controlled.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var guildByName = guildSystems.ToDictionary(s => SystemNameNormalizer.Normalize(s.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var name in normalized)
        {
            var gs = guildByName.GetValueOrDefault(name);
            var cs = gs != null ? controlledByName.GetValueOrDefault(gs.Name) : null;
            var dto = gs != null ? ToDto(gs, cs) : null;
            var inaraVal = inaraByName.TryGetValue(name, out var v) ? v : (decimal?)null;

            var influencePercent = dto?.InfluencePercent ?? 0;
            var influenceClass = InfluenceThresholds.GetInfluenceClass(influencePercent);

            string categoryDisplay;
            string finalDisplayCategory;
            if (gs == null)
            {
                categoryDisplay = "(absent)";
                finalDisplayCategory = "absent";
            }
            else if (string.Equals(gs.Category, "Origine", StringComparison.OrdinalIgnoreCase))
            {
                categoryDisplay = "Origine";
                finalDisplayCategory = "origin";
            }
            else if (cs?.IsHeadquarter == true)
            {
                categoryDisplay = "Quartier général";
                finalDisplayCategory = "headquarter";
            }
            else if (cs?.IsThreatened == true || cs?.IsExpansionCandidate == true)
            {
                categoryDisplay = "Systèmes critiques";
                finalDisplayCategory = "critical";
            }
            else
            {
                categoryDisplay = "Autres";
                finalDisplayCategory = "others";
            }

            result.Add(new GuildSystemAuditEntry(
                RequestedName: name,
                Found: gs != null,
                InaraInfluencePercent: inaraVal,
                RawInaraInfluence: inaraVal,
                ParsedInfluence: gs?.InfluencePercent,
                GuildSystemId: gs?.Id,
                GuildSystemInfluencePercent: gs?.InfluencePercent,
                GuildSystemCategory: gs?.Category,
                ControlledSystemInfluencePercent: cs?.InfluencePercent,
                ControlledSystemState: cs?.State,
                ControlledSystemIsThreatened: cs?.IsThreatened,
                ControlledSystemIsExpansionCandidate: cs?.IsExpansionCandidate,
                ControlledSystemIsHeadquarter: cs?.IsHeadquarter,
                DtoInfluencePercent: dto?.InfluencePercent,
                DtoState: dto?.State,
                CategoryDisplay: categoryDisplay,
                FinalDisplayCategory: finalDisplayCategory,
                InfluenceClass: influenceClass,
                SourceUsed: "GuildSystem"
            ));
        }

        return result;
    }

    /// <summary>Toggle HQ : si le système n'est pas HQ, le définit comme HQ (et retire les autres). S'il est déjà HQ, retire le statut.</summary>
    public async Task<bool> ToggleHeadquarterAsync(int guildSystemId, int guildId, CancellationToken ct = default)
    {
        var gs = await _db.GuildSystems
            .FirstOrDefaultAsync(s => s.Id == guildSystemId && s.GuildId == guildId, ct);
        if (gs == null)
        {
            var csById = await _db.ControlledSystems
                .FirstOrDefaultAsync(c => c.Id == guildSystemId && c.GuildId == guildId, ct);
            if (csById != null)
                gs = await _db.GuildSystems
                    .FirstOrDefaultAsync(s => s.GuildId == guildId && s.Name == csById.Name, ct);
        }
        if (gs == null)
            return false;

        var cs = await _db.ControlledSystems
            .FirstOrDefaultAsync(c => c.GuildId == guildId && c.Name == gs.Name, ct);

        if (cs == null)
        {
            cs = new ControlledSystem
            {
                GuildId = guildId,
                Name = gs.Name,
                InfluencePercent = gs.InfluencePercent,
                IsClean = gs.IsClean,
                Category = gs.Category,
                IsControlled = false,
                IsHeadquarter = true,
                IsFromSeed = true,
                IsUnderSurveillance = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _db.ControlledSystems.Add(cs);
        }
        else
        {
            var wasHq = cs.IsHeadquarter;
            cs.IsHeadquarter = !wasHq;
            cs.UpdatedAt = DateTime.UtcNow;

            if (!wasHq)
            {
                var othersHq = await _db.ControlledSystems
                    .Where(c => c.GuildId == guildId && c.Id != cs.Id && c.IsHeadquarter)
                    .ToListAsync(ct);
                foreach (var o in othersHq)
                {
                    o.IsHeadquarter = false;
                    o.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Toggle Surveillance : ajoute ou retire le système de la section "Systèmes sous surveillance". Même logique que HQ (plusieurs autorisés).</summary>
    public async Task<bool> ToggleSurveillanceAsync(int guildSystemId, int guildId, CancellationToken ct = default)
    {
        var gs = await _db.GuildSystems
            .FirstOrDefaultAsync(s => s.Id == guildSystemId && s.GuildId == guildId, ct);
        if (gs == null)
        {
            var csById = await _db.ControlledSystems
                .FirstOrDefaultAsync(c => c.Id == guildSystemId && c.GuildId == guildId, ct);
            if (csById != null)
                gs = await _db.GuildSystems
                    .FirstOrDefaultAsync(s => s.GuildId == guildId && s.Name == csById.Name, ct);
        }
        if (gs == null)
            return false;

        var cs = await _db.ControlledSystems
            .FirstOrDefaultAsync(c => c.GuildId == guildId && c.Name == gs.Name, ct);

        if (cs == null)
        {
            cs = new ControlledSystem
            {
                GuildId = guildId,
                Name = gs.Name,
                InfluencePercent = gs.InfluencePercent,
                IsClean = gs.IsClean,
                Category = gs.Category,
                IsControlled = false,
                IsHeadquarter = false,
                IsFromSeed = true,
                IsUnderSurveillance = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _db.ControlledSystems.Add(cs);
        }
        else
        {
            cs.IsUnderSurveillance = !cs.IsUnderSurveillance;
            cs.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static GuildSystemBgsDto ToDto(GuildSystem gs, ControlledSystem? cs)
    {
        // InfluenceDelta72h : source EDSM (enrichissement). Jamais seed.
        decimal? delta = (cs != null && !cs.IsFromSeed && cs.InfluenceDelta72h != null) ? cs.InfluenceDelta72h : null;

        var state = cs?.State;
        if (string.Equals(state, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(state))
            state = null;

        var states = state != null
            ? state.Split(',', StringSplitOptions.TrimEntries).Where(s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, "None", StringComparison.OrdinalIgnoreCase)).ToList()
            : (IReadOnlyList<string>?)null;

        var isFromSeed = cs?.IsFromSeed ?? true;
        return new GuildSystemBgsDto(
            gs.Id,
            gs.Name,
            gs.InfluencePercent,
            delta,
            state,
            states?.Count > 0 ? states : null,
            cs?.IsThreatened ?? false,
            cs?.IsExpansionCandidate ?? false,
            cs?.IsHeadquarter ?? false,
            cs?.IsUnderSurveillance ?? false,
            cs?.IsClean ?? gs.IsClean,
            gs.Category,
            cs?.LastUpdated,
            isFromSeed,
            gs.InaraUrl,
            gs.CoordsX,
            gs.CoordsY,
            gs.CoordsZ,
            gs.PrimaryStarClass
        );
    }
}
