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
            var isConflict = IsConflictState(dto.State);
            var influence = dto.InfluencePercent;
            var isCritical = influence < TacticalCritical;
            var isLow = influence < TacticalLow && !isCritical;
            var isHealthy = influence >= TacticalHigh;

            if (cs != null && !cs.IsFromSeed)
                anyFromSync = true;

            // Priorité unique : Origine > HQ > Conflits > Critiques > Bas > Sains > Autres
            if (isOrigin)
                origin.Add(dto);
            else if (isHq)
                headquarter.Add(dto);
            else if (isConflict)
                conflicts.Add(dto);
            else if (isCritical)
                critical.Add(dto);
            else if (isLow)
                low.Add(dto);
            else if (isHealthy)
                healthy.Add(dto);
            else
                others.Add(dto);
        }

        conflicts = conflicts.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();
        critical = critical.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();
        low = low.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();
        healthy = healthy.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();
        others = others.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();

        var dataSource = anyFromSync ? "cached" : "seed";
        var thresholds = new InfluenceThresholdsDto(
            InfluenceThresholds.Critical, InfluenceThresholds.Low, InfluenceThresholds.High);
        var tactical = new TacticalThresholdsDto(TacticalCritical, TacticalLow, TacticalHigh);
        var result = new GuildSystemsResponseDto(origin, headquarter, conflicts, critical, low, healthy, others, dataSource, thresholds, tactical);
        return result;
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
        return new GuildSystemsResponseDto([], [], [], [], [], [], [], "seed", th, tactical);
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

    private static GuildSystemBgsDto ToDto(GuildSystem gs, ControlledSystem? cs)
    {
        // InfluenceDelta24h : source Inara (import) ou sync BGS. Jamais seed.
        decimal? delta = (cs != null && !cs.IsFromSeed && cs.InfluenceDelta24h != null) ? cs.InfluenceDelta24h : null;

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
            cs?.IsClean ?? gs.IsClean,
            gs.Category,
            cs?.LastUpdated,
            isFromSeed
        );
    }
}
