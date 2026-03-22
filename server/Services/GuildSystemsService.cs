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

    public GuildSystemsService(GuildDashboardDbContext db, InaraFactionService inaraFaction)
    {
        _db = db;
        _inaraFaction = inaraFaction;
    }

    public async Task<GuildSystemsResponseDto> GetSystemsAsync(int guildId = 1)
    {
        var guildExists = await _db.Guilds.AnyAsync(g => g.Id == guildId);
        if (!guildExists)
            return new GuildSystemsResponseDto([], [], [], [], "seed", new InfluenceThresholdsDto(
                InfluenceThresholds.Critical, InfluenceThresholds.Low, InfluenceThresholds.High));

        var guildSystems = await _db.GuildSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync();
        var controlled = await _db.ControlledSystems
            .Where(s => s.GuildId == guildId)
            .ToListAsync();
        var controlledByName = controlled.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var origin = new List<GuildSystemBgsDto>();
        var headquarter = new List<GuildSystemBgsDto>();
        var critical = new List<GuildSystemBgsDto>();
        var others = new List<GuildSystemBgsDto>();
        var anyFromSync = false;

        foreach (var gs in guildSystems)
        {
            var cs = controlledByName.GetValueOrDefault(gs.Name);
            var dto = ToDto(gs, cs);
            var isOrigin = string.Equals(gs.Category, "Origine", StringComparison.OrdinalIgnoreCase);
            var isHq = cs?.IsHeadquarter == true;
            var isCritical = cs?.IsThreatened == true || cs?.IsExpansionCandidate == true;

            if (cs != null && !cs.IsFromSeed)
                anyFromSync = true;

            // Une seule catégorie par système : Origine > HQ > Critiques > Autres
            if (isOrigin)
                origin.Add(dto);
            else if (isHq)
                headquarter.Add(dto);
            else if (isCritical)
                critical.Add(dto);
            else
                others.Add(dto);
        }

        critical = critical.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();
        others = others.OrderByDescending(s => s.InfluencePercent).ThenBy(s => s.Name).ToList();

        var dataSource = anyFromSync ? "cached" : "seed";
        var thresholds = new InfluenceThresholdsDto(
            InfluenceThresholds.Critical, InfluenceThresholds.Low, InfluenceThresholds.High);
        return new GuildSystemsResponseDto(origin, headquarter, critical, others, dataSource, thresholds);
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
        // InfluenceDelta24h : n'afficher que si source réelle (sync). Jamais de valeur seed trompeuse.
        decimal? delta = (cs != null && !cs.IsFromSeed && cs.InfluenceDelta24h != null) ? cs.InfluenceDelta24h : null;

        // Ne pas afficher State = "None" (BGS "aucun état") pour éviter confusion avec "non"
        var state = cs?.State;
        if (string.Equals(state, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(state))
            state = null;

        // Source de vérité pour l'influence : GuildSystem (alimenté par Inara import).
        // ControlledSystem.InfluencePercent peut être périmé (BgsSync ne le met pas à jour).
        var isFromSeed = cs?.IsFromSeed ?? true;
        return new GuildSystemBgsDto(
            gs.Id,
            gs.Name,
            gs.InfluencePercent,
            delta,
            state,
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
