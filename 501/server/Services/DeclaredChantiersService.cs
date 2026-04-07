using System.Text.Json;
using System.Text.Json.Serialization;
using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Résultat détaillé suppression (diagnostic + cluster doublons métier).</summary>
public sealed record DeclaredChantierDeleteOutcome(
    bool AnchorFound,
    int RequestedId,
    int GuildId,
    IReadOnlyList<int> DeletedIds,
    int SaveChangesAffected,
    bool RequestedIdStillExistsAfter,
    string LogAnchorLine);

/// <summary>
/// Persistance chantiers déclarés — table SQL <c>DeclaredChantiers</c> uniquement (pas de cache, pas de fusion Frontier live).
/// Upsert : clé marché CAPI <see cref="DeclaredChantier.MarketId"/> si fourni, sinon même site (SystemNameKey+StationNameKey) + même CMDR.
/// </summary>
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

    /// <summary>
    /// Lecture exclusive de la table <c>DeclaredChantiers</c> (EF <c>AsNoTracking</c>), filtre <c>Active</c> — aucune autre source.
    /// </summary>
    public async Task<IReadOnlyList<DeclaredChantierListItemDto>> GetActiveForGuildAsync(int guildId, CancellationToken ct = default)
    {
        _log.LogDebug(
            "[DeclaredChantiers] GET source=table DeclaredChantiers only (guild={Guild}, active=true, AsNoTracking)",
            guildId);

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

    /// <summary>Logs temporaires : liste renvoyée + détection doublons métier (scope GET).</summary>
    public void LogGetChantiersResponseDiagnostics(string scope, int guildId, IReadOnlyList<DeclaredChantierListItemDto> list)
    {
        _log.LogInformation(
            "GET chantiers source=DeclaredChantiers(SQL) scope={Scope} guild={Guild} count={Count} ids=[{Ids}]",
            scope,
            guildId,
            list.Count,
            list.Count == 0 ? "" : string.Join(',', list.Select(x => x.Id)));

        foreach (var x in list)
        {
            _log.LogInformation(
                "GET chantier item id={Id} marketId(CAPI)={Mid} system={Sys} station={St} commander={Cmdr} active={Active} guild={Guild}",
                x.Id,
                x.MarketId ?? "(null)",
                x.SystemName,
                x.StationName,
                x.CmdrName,
                x.Active,
                guildId);
        }

        var bySiteCmdr = list.GroupBy(x => (
            Sys: NormalizeKey(x.SystemName),
            St: NormalizeKey(x.StationName),
            Cmdr: NormalizeCmdrName(x.CmdrName)));
        foreach (var grp in bySiteCmdr.Where(g => g.Count() > 1))
        {
            var first = grp.First();
            _log.LogWarning(
                "DUPLICATE métier scope={Scope} même site+cmdr ids=[{Ids}] system={Sys} station={St} commander={Cmdr}",
                scope,
                string.Join(',', grp.Select(x => x.Id)),
                first.SystemName,
                first.StationName,
                first.CmdrName);
        }

        var byMarket = list
            .Where(x => !string.IsNullOrWhiteSpace(x.MarketId))
            .GroupBy(x => x.MarketId!.Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (var grp in byMarket.Where(g => g.Count() > 1))
        {
            _log.LogWarning(
                "DUPLICATE métier scope={Scope} même marketId ids=[{Ids}] marketId={Mid}",
                scope,
                string.Join(',', grp.Select(x => x.Id)),
                grp.Key);
        }
    }

    /// <summary>
    /// Après DELETE réussi : même requêtes que les GET me / others (données issues uniquement de DeclaredChantiers), pour prouver que les ids supprimés ne reviennent pas.
    /// </summary>
    public async Task LogPostDeleteMeAndOthersSnapshotAsync(
        int guildId,
        string? commanderName,
        IReadOnlyList<int> deletedIds,
        CancellationToken ct)
    {
        IReadOnlyList<DeclaredChantierListItemDto> me;
        if (string.IsNullOrWhiteSpace(commanderName))
            me = Array.Empty<DeclaredChantierListItemDto>();
        else
            me = await GetActiveForGuildForCommanderAsync(guildId, commanderName, ct);

        var others = await GetActiveForGuildExcludingCommanderAsync(guildId, commanderName, ct);

        foreach (var delId in deletedIds)
        {
            var inMe = me.Any(x => x.Id == delId);
            var inOthers = others.Any(x => x.Id == delId);
            _log.LogInformation(
                "DELETE chantier post-verify id={Id} still in scope_me={InMe} scope_others={InOthers} (attendu false/false)",
                delId,
                inMe,
                inOthers);
        }

        LogGetChantiersResponseDiagnostics("me-after-delete", guildId, me);
        LogGetChantiersResponseDiagnostics("others-after-delete", guildId, others);
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
        var cmdrKey = NormalizeCmdrName(cmdr);

        var json = SerializeResources(req.ConstructionResources, req.ConstructionResourcesTotal);
        var now = DateTime.UtcNow;

        DeclaredChantier? row = null;
        var matchReason = "none";

        if (marketId != null)
        {
            row = await _db.DeclaredChantiers
                .FirstOrDefaultAsync(x => x.GuildId == guildId && x.MarketId != null && x.MarketId == marketId, ct);
            if (row != null)
                matchReason = "guild+marketId(CAPI)";
        }

        if (row == null)
        {
            var atSite = await _db.DeclaredChantiers
                .Where(x => x.GuildId == guildId && x.SystemNameKey == sysKey && x.StationNameKey == stKey)
                .ToListAsync(ct);
            row = atSite.FirstOrDefault(x => NormalizeCmdrName(x.CmdrName) == cmdrKey);
            if (row != null)
                matchReason = "guild+systemKey+stationKey+cmdr (fusionne MarketId null/non-null)";
        }

        if (row != null)
        {
            _log.LogInformation(
                "UPSERT chantier match on [{Match}] => existing id={Id} (guild={Guild} system={Sys} station={St} cmdr={Cmdr} marketIdRow={MidRow} marketIdReq={MidReq})",
                matchReason,
                row.Id,
                guildId,
                system,
                station,
                cmdr,
                row.MarketId ?? "(null)",
                marketId ?? "(null)");
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
            _log.LogInformation(
                "UPSERT chantier no existing row found => create new (guild={Guild} system={Sys} station={St} cmdr={Cmdr} marketId={Mid})",
                guildId,
                system,
                station,
                cmdr,
                marketId ?? "(null)");
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

        var isNew = row.Id == 0;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation(
            isNew
                ? "[DeclaredChantiers] Création chantier id={Id} guild={Guild} marketId={MarketId}"
                : "[DeclaredChantiers] Mise à jour chantier id={Id} guild={Guild} marketId={MarketId}",
            row.Id,
            guildId,
            marketId ?? "(null)");

        return ToListItem(row);
    }

    /// <summary>
    /// Supprime la ligne demandée et toute ligne doublon métier (même guilde, même site, même CMDR) — cas typique : MarketId null + ligne avec MarketId CAPI.
    /// </summary>
    public async Task<DeclaredChantierDeleteOutcome> DeleteClusterByPrimaryIdAsync(int guildId, int requestedId, CancellationToken ct = default)
    {
        _log.LogInformation("DELETE chantier requested id={Id} guild={Guild}", requestedId, guildId);

        var anchor = await _db.DeclaredChantiers.FirstOrDefaultAsync(x => x.GuildId == guildId && x.Id == requestedId, ct);
        if (anchor == null)
        {
            _log.LogWarning("DELETE chantier anchor NOT FOUND id={Id} guild={Guild}", requestedId, guildId);
            return new DeclaredChantierDeleteOutcome(
                false,
                requestedId,
                guildId,
                Array.Empty<int>(),
                0,
                false,
                "not found");
        }

        var cmdrK = NormalizeCmdrName(anchor.CmdrName);
        var atSite = await _db.DeclaredChantiers
            .Where(x => x.GuildId == guildId
                && x.SystemNameKey == anchor.SystemNameKey
                && x.StationNameKey == anchor.StationNameKey)
            .ToListAsync(ct);
        var cluster = atSite.Where(x => NormalizeCmdrName(x.CmdrName) == cmdrK).ToList();

        var anchorLine =
            $"DELETE chantier found id={anchor.Id} marketId(CAPI)={anchor.MarketId ?? "(null)"} system={anchor.SystemName} station={anchor.StationName} commander={anchor.CmdrName} active={anchor.Active} guild={guildId} keys=({anchor.SystemNameKey}|{anchor.StationNameKey})";
        _log.LogInformation("{Line}", anchorLine);
        _log.LogInformation(
            "DELETE chantier cluster same site+cmdr: ids=[{Ids}] count={Count}",
            string.Join(',', cluster.Select(c => c.Id)),
            cluster.Count);

        _db.DeclaredChantiers.RemoveRange(cluster);
        _log.LogInformation("DELETE chantier RemoveRange pending ids=[{Ids}] — calling SaveChanges", string.Join(',', cluster.Select(c => c.Id)));

        var affected = await _db.SaveChangesAsync(ct);
        _log.LogInformation("DELETE chantier SaveChanges affected={Affected}", affected);

        var stillThere = await _db.DeclaredChantiers.AsNoTracking().AnyAsync(x => x.Id == requestedId, ct);
        _log.LogInformation("DELETE chantier verify by id={Id} exists={Exists}", requestedId, stillThere);
        if (affected == 0)
            _log.LogWarning("DELETE chantier SaveChanges reported 0 entities changed — unexpected after RemoveRange");

        foreach (var delId in cluster.Select(c => c.Id))
        {
            var exists = await _db.DeclaredChantiers.AsNoTracking().AnyAsync(x => x.Id == delId, ct);
            if (exists)
                _log.LogWarning("DELETE chantier verify FAILED id={Id} still present in table", delId);
        }

        var remainingDupHint = await _db.DeclaredChantiers
            .AsNoTracking()
            .Where(x => x.GuildId == guildId
                && x.Active
                && x.SystemNameKey == anchor.SystemNameKey
                && x.StationNameKey == anchor.StationNameKey)
            .Select(x => new { x.Id, x.CmdrName, x.MarketId })
            .ToListAsync(ct);
        var sameCmdrLeft = remainingDupHint.Where(x => NormalizeCmdrName(x.CmdrName) == cmdrK).ToList();
        if (sameCmdrLeft.Count > 0)
            _log.LogWarning(
                "DELETE chantier post-check: still {Count} active row(s) same site+cmdr ids=[{Ids}]",
                sameCmdrLeft.Count,
                string.Join(',', sameCmdrLeft.Select(x => x.Id)));
        else
            _log.LogInformation(
                "DELETE chantier post-check: no active row left same site+cmdr for commander={Cmdr}",
                anchor.CmdrName);

        var allActiveIds = await _db.DeclaredChantiers.AsNoTracking()
            .Where(x => x.GuildId == guildId && x.Active)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync(ct);
        _log.LogInformation(
            "DELETE chantier post-check guild active ids=[{Ids}] count={Count} (table DeclaredChantiers)",
            allActiveIds.Count == 0 ? "" : string.Join(',', allActiveIds),
            allActiveIds.Count);

        return new DeclaredChantierDeleteOutcome(
            true,
            requestedId,
            guildId,
            cluster.Select(c => c.Id).ToList(),
            affected,
            stillThere,
            anchorLine);
    }

    /// <summary>Compat : supprime le cluster ; considéré OK si ancrage trouvé.</summary>
    public async Task<bool> DeleteByIdAsync(int guildId, int id, CancellationToken ct = default)
    {
        var o = await DeleteClusterByPrimaryIdAsync(guildId, id, ct);
        return o.AnchorFound;
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
