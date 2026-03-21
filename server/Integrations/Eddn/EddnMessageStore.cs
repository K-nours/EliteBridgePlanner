using GuildDashboard.Server.Data;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Integrations.Eddn;

/// <summary>Stockage brut des messages EDDN en base.</summary>
public class EddnMessageStore
{
    private readonly GuildDashboardDbContext _db;
    private readonly ILogger<EddnMessageStore> _log;

    public EddnMessageStore(GuildDashboardDbContext db, ILogger<EddnMessageStore> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<long> StoreAsync(EddnRawMessage msg, CancellationToken ct = default)
    {
        _db.EddnRawMessages.Add(msg);
        await _db.SaveChangesAsync(ct);
        _log.LogDebug("EDDN message stored Id={Id} SchemaRef={SchemaRef}", msg.Id, msg.SchemaRef);
        return msg.Id;
    }

    public async Task<long> GetCountAsync(CancellationToken ct = default) =>
        await _db.EddnRawMessages.LongCountAsync(ct);

    public async Task<IReadOnlyList<string>> GetLastSchemaRefsAsync(int take = 20, CancellationToken ct = default)
    {
        return await _db.EddnRawMessages
            .AsNoTracking()
            .Where(m => m.SchemaRef != null)
            .OrderByDescending(m => m.ReceivedAt)
            .Select(m => m.SchemaRef!)
            .Distinct()
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<DateTime?> GetLastReceivedAtAsync(CancellationToken ct = default)
    {
        var last = await _db.EddnRawMessages
            .AsNoTracking()
            .OrderByDescending(m => m.ReceivedAt)
            .Select(m => m.ReceivedAt)
            .FirstOrDefaultAsync(ct);
        return last == default ? null : last;
    }

    public async Task<IReadOnlyList<EddnRawMessage>> GetMessagesAsync(int limit = 50, CancellationToken ct = default)
    {
        return await _db.EddnRawMessages
            .AsNoTracking()
            .OrderByDescending(m => m.ReceivedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>Fréquence par SchemaRef (triée par count décroissant).</summary>
    public async Task<IReadOnlyList<(string SchemaRef, long Count)>> GetSchemaRefFrequencyAsync(int take = 50, CancellationToken ct = default)
    {
        var list = await _db.EddnRawMessages
            .AsNoTracking()
            .Where(m => m.SchemaRef != null)
            .GroupBy(m => m.SchemaRef!)
            .Select(g => new { SchemaRef = g.Key!, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .Take(take)
            .ToListAsync(ct);
        return list.Select(x => (x.SchemaRef, x.Count)).ToList();
    }

    /// <summary>Top systèmes par fréquence.</summary>
    public async Task<IReadOnlyList<(string SystemName, long Count)>> GetTopSystemsAsync(int take = 50, CancellationToken ct = default)
    {
        var list = await _db.EddnRawMessages
            .AsNoTracking()
            .Where(m => m.SystemName != null && m.SystemName != "")
            .GroupBy(m => m.SystemName!)
            .Select(g => new { SystemName = g.Key!, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .Take(take)
            .ToListAsync(ct);
        return list.Select(x => (x.SystemName, x.Count)).ToList();
    }

    /// <summary>Top stations par fréquence.</summary>
    public async Task<IReadOnlyList<(string StationName, long Count)>> GetTopStationsAsync(int take = 50, CancellationToken ct = default)
    {
        var list = await _db.EddnRawMessages
            .AsNoTracking()
            .Where(m => m.StationName != null && m.StationName != "")
            .GroupBy(m => m.StationName!)
            .Select(g => new { StationName = g.Key!, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .Take(take)
            .ToListAsync(ct);
        return list.Select(x => (x.StationName, x.Count)).ToList();
    }

    /// <summary>Exemples de payload simplifiés par SchemaRef (1 par schéma).</summary>
    public async Task<IReadOnlyList<object>> GetPayloadExamplesAsync(int perSchema = 1, CancellationToken ct = default)
    {
        var schemas = await _db.EddnRawMessages
            .AsNoTracking()
            .Where(m => m.SchemaRef != null)
            .Select(m => m.SchemaRef!)
            .Distinct()
            .ToListAsync(ct);

        var examples = new List<object>();
        foreach (var schema in schemas)
        {
            var samples = await _db.EddnRawMessages
                .AsNoTracking()
                .Where(m => m.SchemaRef == schema)
                .OrderByDescending(m => m.ReceivedAt)
                .Take(perSchema)
                .Select(m => new { m.SchemaRef, m.SystemName, m.StationName, m.SourceSoftware, m.ReceivedAt, m.MessageJson })
                .ToListAsync(ct);

            foreach (var s in samples)
            {
                var simplified = SimplifyPayload(s.MessageJson);
                examples.Add(new { s.SchemaRef, s.SystemName, s.StationName, s.SourceSoftware, s.ReceivedAt, simplified });
            }
        }

        return examples;
    }

    private static object? SimplifyPayload(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var dict = new Dictionary<string, object?>();

            if (root.TryGetProperty("$schemaRef", out var sr))
                dict["$schemaRef"] = sr.GetString();
            if (root.TryGetProperty("header", out var h))
                dict["header"] = JsonElementToSimpleObject(h);
            if (root.TryGetProperty("message", out var m))
                dict["message"] = JsonElementToSimpleObject(m);

            return dict;
        }
        catch
        {
            return new { raw = json.Length > 500 ? json[..500] + "..." : json };
        }
    }

    /// <summary>Pour chaque schéma : couverture systemName, stationName, champs BGS (factions, influence, state).</summary>
    public async Task<IReadOnlyList<EddnSchemaCoverage>> GetSchemaFieldCoverageAsync(CancellationToken ct = default)
    {
        var schemas = await _db.EddnRawMessages
            .AsNoTracking()
            .Where(m => m.SchemaRef != null)
            .Select(m => m.SchemaRef!)
            .Distinct()
            .ToListAsync(ct);

        var result = new List<EddnSchemaCoverage>();
        foreach (var schemaRef in schemas)
        {
            var withSystem = await _db.EddnRawMessages
                .AsNoTracking()
                .Where(m => m.SchemaRef == schemaRef && m.SystemName != null && m.SystemName != "")
                .LongCountAsync(ct);
            var withStation = await _db.EddnRawMessages
                .AsNoTracking()
                .Where(m => m.SchemaRef == schemaRef && m.StationName != null && m.StationName != "")
                .LongCountAsync(ct);
            var total = await _db.EddnRawMessages
                .AsNoTracking()
                .Where(m => m.SchemaRef == schemaRef)
                .LongCountAsync(ct);

            var samples = await _db.EddnRawMessages
                .AsNoTracking()
                .Where(m => m.SchemaRef == schemaRef)
                .OrderByDescending(m => m.ReceivedAt)
                .Take(5)
                .Select(m => m.MessageJson)
                .ToListAsync(ct);

            var (hasFactions, hasInfluence, hasState) = ScanForBgsFields(samples);

            result.Add(new EddnSchemaCoverage
            {
                SchemaRef = schemaRef,
                TotalCount = total,
                WithSystemName = withSystem,
                WithStationName = withStation,
                HasFactions = hasFactions,
                HasInfluence = hasInfluence,
                HasState = hasState,
            });
        }

        return result.OrderByDescending(x => x.TotalCount).ToList();
    }

    private static (bool hasFactions, bool hasInfluence, bool hasState) ScanForBgsFields(IEnumerable<string> jsons)
    {
        var hasFactions = false;
        var hasInfluence = false;
        var hasState = false;
        foreach (var json in jsons)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("message", out var msg))
                {
                    foreach (var p in msg.EnumerateObject())
                    {
                        var name = p.Name.ToLowerInvariant();
                        if (name.Contains("faction")) hasFactions = true;
                        if (name.Contains("influence")) hasInfluence = true;
                        if (name == "state" || name == "factionstate") hasState = true;
                    }
                }
            }
            catch { /* ignore parse errors */ }
        }
        return (hasFactions, hasInfluence, hasState);
    }

    private static object? JsonElementToSimpleObject(System.Text.Json.JsonElement el)
    {
        return el.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Object => el.EnumerateObject()
                .ToDictionary(p => p.Name, p => JsonElementToSimpleObject(p.Value)),
            System.Text.Json.JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToSimpleObject).ToList(),
            System.Text.Json.JsonValueKind.String => el.GetString(),
            System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out var i) ? i : el.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            _ => el.ToString(),
        };
    }
}
