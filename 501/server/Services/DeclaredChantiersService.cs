using System.Text.Json;
using System.Text.Json.Serialization;
using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Persistance chantiers déclarés — upsert par MarketId ou (système, station).</summary>
public class DeclaredChantiersService
{
    private const int MaxJsonChars = 48_000;

    private static readonly JsonSerializerOptions JsonStorageOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly GuildDashboardDbContext _db;
    private readonly ILogger<DeclaredChantiersService> _log;

    public DeclaredChantiersService(GuildDashboardDbContext db, ILogger<DeclaredChantiersService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<IReadOnlyList<DeclaredChantierListItemDto>> GetActiveForGuildAsync(int guildId, CancellationToken ct = default)
    {
        var rows = await _db.DeclaredChantiers
            .AsNoTracking()
            .Where(x => x.GuildId == guildId && x.Active)
            .OrderBy(x => x.SystemName)
            .ThenBy(x => x.StationName)
            .ToListAsync(ct);

        return rows.Select(ToListItem).ToList();
    }

    /// <summary>
    /// Chantiers actifs dont le <see cref="DeclaredChantier.CmdrName"/> correspond au commandant courant (comparaison insensible à la casse).
    /// </summary>
    public async Task<IReadOnlyList<DeclaredChantierListItemDto>> GetActiveForGuildForCommanderAsync(
        int guildId,
        string commanderName,
        CancellationToken ct = default)
    {
        var key = NormalizeCmdrName(commanderName);
        if (string.IsNullOrEmpty(key))
            return Array.Empty<DeclaredChantierListItemDto>();

        var all = await GetActiveForGuildAsync(guildId, ct);
        return all.Where(x => NormalizeCmdrName(x.CmdrName) == key).ToList();
    }

    /// <summary>
    /// Chantiers actifs dont le CMDR n’est pas le commandant courant. Si <paramref name="commanderName"/> est vide, renvoie toute la liste active (identité « moi » indéterminée).
    /// </summary>
    public async Task<IReadOnlyList<DeclaredChantierListItemDto>> GetActiveForGuildExcludingCommanderAsync(
        int guildId,
        string? commanderName,
        CancellationToken ct = default)
    {
        var key = NormalizeCmdrName(commanderName);
        var all = await GetActiveForGuildAsync(guildId, ct);
        if (string.IsNullOrEmpty(key))
            return all;

        return all.Where(x => NormalizeCmdrName(x.CmdrName) != key).ToList();
    }

    private static string NormalizeCmdrName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;
        return s.Trim().ToLowerInvariant();
    }

    public async Task<DeclaredChantierListItemDto> UpsertAsync(int guildId, DeclaredChantierPersistRequest req, CancellationToken ct = default)
    {
        var system = req.SystemName.Trim();
        var station = req.StationName.Trim();
        var marketId = string.IsNullOrWhiteSpace(req.MarketId) ? null : req.MarketId.Trim();
        var sysKey = NormalizeKey(system);
        var stKey = NormalizeKey(station);
        var cmdr = (req.CommanderName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(cmdr)) cmdr = "—";

        var json = SerializeResources(req.ConstructionResources, req.ConstructionResourcesTotal);
        var now = DateTime.UtcNow;

        DeclaredChantier? row = null;
        if (marketId != null)
        {
            row = await _db.DeclaredChantiers
                .FirstOrDefaultAsync(x => x.GuildId == guildId && x.MarketId == marketId, ct);
        }

        row ??= await _db.DeclaredChantiers
            .FirstOrDefaultAsync(
                x => x.GuildId == guildId
                    && x.MarketId == null
                    && x.SystemNameKey == sysKey
                    && x.StationNameKey == stKey,
                ct);

        if (row != null)
        {
            row.CmdrName = cmdr;
            row.SystemName = system;
            row.StationName = station;
            row.MarketId = marketId;
            row.SystemNameKey = sysKey;
            row.StationNameKey = stKey;
            row.Active = true;
            row.UpdatedAtUtc = now;
            row.ConstructionResourcesJson = json;
        }
        else
        {
            row = new DeclaredChantier
            {
                GuildId = guildId,
                CmdrName = cmdr,
                SystemName = system,
                StationName = station,
                MarketId = marketId,
                SystemNameKey = sysKey,
                StationNameKey = stKey,
                Active = true,
                DeclaredAtUtc = now,
                UpdatedAtUtc = now,
                ConstructionResourcesJson = json,
            };
            _db.DeclaredChantiers.Add(row);
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation(
            "[DeclaredChantiers] Upsert id={Id} guild={Guild} marketId={MarketId}",
            row.Id,
            guildId,
            marketId ?? "(null)");

        return ToListItem(row);
    }

    /// <summary>
    /// Lignes actives dont le marché CAPI correspond (marketId prioritaire, sinon système profil + station).
    /// </summary>
    public static List<DeclaredChantier> FindRowsMatchingMarketSummary(
        IReadOnlyList<DeclaredChantier> candidates,
        FrontierMarketBusinessSummary market,
        string? profileSystemKey)
    {
        var mId = string.IsNullOrWhiteSpace(market.MarketId) ? null : market.MarketId.Trim();
        var stKey = NormalizeKey(market.StationName ?? "");
        var sysKey = string.IsNullOrWhiteSpace(profileSystemKey) ? null : NormalizeKey(profileSystemKey);

        if (mId != null)
        {
            var byMid = candidates
                .Where(r => r.MarketId != null && string.Equals(r.MarketId.Trim(), mId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byMid.Count > 0)
                return byMid;
        }

        if (sysKey != null && !string.IsNullOrEmpty(stKey))
        {
            return candidates
                .Where(r => r.SystemNameKey == sysKey && r.StationNameKey == stKey)
                .ToList();
        }

        return new List<DeclaredChantier>();
    }

    /// <summary>Applique le résumé marché à des lignes suivies — désactive si tout est livré (remaining ≤ 0).</summary>
    public async Task<(int Updated, int Deactivated)> ApplyMarketSummaryToRowsAsync(
        IReadOnlyList<DeclaredChantier> rows,
        FrontierMarketBusinessSummary market,
        CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return (0, 0);

        if (!market.HasConstructionResources || market.ConstructionResourcesCount <= 0)
            return (0, 0);

        var dtos = market.ConstructionResources
            .Select(x => new DeclaredChantierResourceDto(x.Name, x.Required, x.Provided, x.Remaining))
            .ToList();
        var json = SerializeResources(dtos, market.ConstructionResourcesCount);
        var now = DateTime.UtcNow;
        var mId = string.IsNullOrWhiteSpace(market.MarketId) ? null : market.MarketId.Trim();

        var deactivated = 0;
        foreach (var row in rows)
        {
            row.ConstructionResourcesJson = json;
            row.UpdatedAtUtc = now;
            if (string.IsNullOrWhiteSpace(row.MarketId) && mId != null)
                row.MarketId = mId;

            if (IsConstructionFullyDelivered(dtos))
            {
                row.Active = false;
                deactivated++;
            }
        }

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "[DeclaredChantiers] ApplyMarket rows={Rows} resources={Res} deactivated={Off} marketId={Mid}",
            rows.Count,
            dtos.Count,
            deactivated,
            mId ?? "(null)");

        return (rows.Count, deactivated);
    }

    private static bool IsConstructionFullyDelivered(IReadOnlyList<DeclaredChantierResourceDto> dtos)
    {
        if (dtos.Count == 0)
            return false;
        return dtos.All(r => r.Remaining <= 0);
    }

    public async Task<DeclaredChantier?> GetActiveTrackedByIdAsync(int guildId, int id, CancellationToken ct = default) =>
        await _db.DeclaredChantiers
            .FirstOrDefaultAsync(x => x.GuildId == guildId && x.Id == id && x.Active, ct);

    /// <summary>Lignes actives suivies (entités trackées) — refresh CAPI par lot.</summary>
    public async Task<List<DeclaredChantier>> GetActiveTrackedRowsAsync(int guildId, CancellationToken ct = default) =>
        await _db.DeclaredChantiers
            .Where(x => x.GuildId == guildId && x.Active)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

    public async Task<DeclaredChantierListItemDto?> GetListItemDtoByIdAsync(int guildId, int id, CancellationToken ct = default)
    {
        var row = await _db.DeclaredChantiers.AsNoTracking().FirstOrDefaultAsync(x => x.GuildId == guildId && x.Id == id, ct);
        return row == null ? null : ToListItem(row);
    }

    private static DeclaredChantierListItemDto ToListItem(DeclaredChantier x)
    {
        var (list, total) = DeserializeResources(x.ConstructionResourcesJson);
        return new DeclaredChantierListItemDto(
            x.Id,
            x.CmdrName,
            x.SystemName,
            x.StationName,
            x.MarketId,
            x.Active,
            x.DeclaredAtUtc,
            x.UpdatedAtUtc,
            list,
            total);
    }

    private static string NormalizeKey(string s) => s.Trim().ToLowerInvariant();

    private static string? SerializeResources(IReadOnlyList<DeclaredChantierResourceDto>? resources, int? totalHint)
    {
        if (resources == null || resources.Count == 0)
            return null;
        try
        {
            var payload = new
            {
                total = totalHint ?? resources.Count,
                items = resources.Take(120).Select(r => new { r.Name, r.Required, r.Provided, r.Remaining }).ToList(),
            };
            var s = JsonSerializer.Serialize(payload, JsonStorageOptions);
            return s.Length <= MaxJsonChars ? s : s[..MaxJsonChars];
        }
        catch
        {
            return null;
        }
    }

    private static (IReadOnlyList<DeclaredChantierResourceDto> List, int Total) DeserializeResources(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (Array.Empty<DeclaredChantierResourceDto>(), 0);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var total = root.TryGetProperty("total", out var t) && t.TryGetInt32(out var tn) ? tn : 0;
            var list = new List<DeclaredChantierResourceDto>();
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in items.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var name = el.TryGetProperty("Name", out var n) ? n.GetString()
                        : el.TryGetProperty("name", out n) ? n.GetString() : null;
                    if (string.IsNullOrEmpty(name)) continue;
                    long req = 0, prov = 0, rem = 0;
                    if (el.TryGetProperty("Required", out var rq) || el.TryGetProperty("required", out rq))
                    {
                        if (rq.ValueKind == JsonValueKind.Number && rq.TryGetInt64(out var l)) req = l;
                    }
                    if (el.TryGetProperty("Provided", out var pr) || el.TryGetProperty("provided", out pr))
                    {
                        if (pr.ValueKind == JsonValueKind.Number && pr.TryGetInt64(out var l)) prov = l;
                    }
                    if (el.TryGetProperty("Remaining", out var rm) || el.TryGetProperty("remaining", out rm))
                    {
                        if (rm.ValueKind == JsonValueKind.Number && rm.TryGetInt64(out var l)) rem = l;
                    }
                    else
                        rem = Math.Max(0, req - prov);
                    list.Add(new DeclaredChantierResourceDto(name, req, prov, rem));
                }
            }
            if (total <= 0) total = list.Count;
            return (list, total);
        }
        catch
        {
            return (Array.Empty<DeclaredChantierResourceDto>(), 0);
        }
    }
}
