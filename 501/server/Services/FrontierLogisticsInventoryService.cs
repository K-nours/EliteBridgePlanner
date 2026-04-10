using System.Text.Json;
using GuildDashboard.Server.DTOs;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Agrège les commodités en soute depuis le CAPI /profile (vaisseau) et /fleetcarrier (FC).
/// </summary>
public class FrontierLogisticsInventoryService
{
    private readonly FrontierAuthService _auth;
    private readonly ILogger<FrontierLogisticsInventoryService> _log;

    public FrontierLogisticsInventoryService(FrontierAuthService auth, ILogger<FrontierLogisticsInventoryService> log)
    {
        _auth = auth;
        _log = log;
    }

    public async Task<FrontierLogisticsInventoryDto> FetchInventoryAsync(string accessToken, CancellationToken ct = default)
    {
        var dto = new FrontierLogisticsInventoryDto();

        var (shipStatus, shipBody) = await _auth.FetchCapiRawAsync(accessToken, "/profile", ct);
        if (shipStatus != 200 || string.IsNullOrEmpty(shipBody))
        {
            dto.ShipCargoError = shipStatus == 0 ? "Profil Frontier indisponible (réseau)" : $"HTTP {shipStatus}";
            _log.LogWarning("[LogisticsInventory] /profile HTTP {Status}", shipStatus);
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(shipBody);
                MergeCargoArraysFromRoot(doc.RootElement, dto.ShipCargoByName);
            }
            catch (Exception ex)
            {
                dto.ShipCargoError = "Profil illisible";
                _log.LogWarning(ex, "[LogisticsInventory] parse /profile");
            }
        }

        // /fleetcarrier peut être lent ou absent (pas de FC) — timeout long, 404 = pas de FC.
        var (fcStatus, fcBody) = await _auth.FetchCapiRawAsync(accessToken, "/fleetcarrier", TimeSpan.FromSeconds(60), ct);
        if (fcStatus == 404)
        {
            dto.CarrierCargoError = null;
        }
        else if (fcStatus != 200 || string.IsNullOrEmpty(fcBody))
        {
            dto.CarrierCargoError = fcStatus == 0 ? "Fleet Carrier indisponible (réseau)" : $"HTTP {fcStatus}";
            _log.LogWarning("[LogisticsInventory] /fleetcarrier HTTP {Status}", fcStatus);
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(fcBody);
                MergeCargoArraysFromRoot(doc.RootElement, dto.CarrierCargoByName);
            }
            catch (Exception ex)
            {
                dto.CarrierCargoError = "Réponse FC illisible";
                _log.LogWarning(ex, "[LogisticsInventory] parse /fleetcarrier");
            }
        }

        return dto;
    }

    /// <summary>
    /// Parcourt le JSON pour des tableaux <c>cargo</c> (vaisseau / FC) et somme par nom de commodité.
    /// </summary>
    private static void MergeCargoArraysFromRoot(JsonElement root, Dictionary<string, int> dict)
    {
        WalkElement(root, dict, depth: 0);
    }

    private static void AddOrMerge(Dictionary<string, int> dict, string name, int qty)
    {
        var key = name.Trim();
        if (string.IsNullOrEmpty(key)) return;
        string? existingKey = null;
        foreach (var kv in dict)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                existingKey = kv.Key;
                break;
            }
        }

        if (existingKey != null)
            dict[existingKey] = dict[existingKey] + qty;
        else
            dict[key] = qty;
    }

    private static void WalkElement(JsonElement el, Dictionary<string, int> dict, int depth)
    {
        if (depth > 24) return;

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    if (p.NameEquals("cargo") && p.Value.ValueKind == JsonValueKind.Array)
                    {
                        MergeCargoArray(p.Value, dict);
                    }
                    else
                    {
                        WalkElement(p.Value, dict, depth + 1);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    WalkElement(item, dict, depth + 1);
                break;
        }
    }

    private static void MergeCargoArray(JsonElement cargoArray, Dictionary<string, int> dict)
    {
        foreach (var item in cargoArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = ExtractCommodityName(item);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var qty = ExtractQuantity(item);
            if (qty <= 0)
                qty = 1;

            AddOrMerge(dict, name, qty);
        }
    }

    private static string? ExtractCommodityName(JsonElement item)
    {
        foreach (var nk in new[] { "name", "Name", "locName", "LocName" })
        {
            if (item.TryGetProperty(nk, out var n) && n.ValueKind == JsonValueKind.String)
            {
                var s = n.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }

        if (item.TryGetProperty("commodity", out var comm) && comm.ValueKind == JsonValueKind.Object)
        {
            foreach (var nk in new[] { "name", "Name", "locName" })
            {
                if (comm.TryGetProperty(nk, out var n) && n.ValueKind == JsonValueKind.String)
                {
                    var s = n.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }

        return null;
    }

    private static int ExtractQuantity(JsonElement item)
    {
        foreach (var qk in new[] { "qty", "Qty", "quantity", "Quantity", "amount", "Amount", "count", "Count" })
        {
            if (!item.TryGetProperty(qk, out var q)) continue;
            if (q.ValueKind == JsonValueKind.Number && q.TryGetInt32(out var i)) return Math.Max(0, i);
            if (q.ValueKind == JsonValueKind.String && int.TryParse(q.GetString(), out var j)) return Math.Max(0, j);
        }

        return 0;
    }
}
