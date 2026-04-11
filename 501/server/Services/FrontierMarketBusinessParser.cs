using System.Text.Json;
using GuildDashboard.Server.DTOs;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Parse métier CAPI GET /market — station, marketId, ressources de chantier (requiredConstructionResources.commodities).
/// Ne renvoie pas de JSON brut ; listes bornées.
/// </summary>
public static class FrontierMarketBusinessParser
{
    private const int MaxBodyChars = 1_500_000;
    private const int MaxCommodityNameChars = 128;
    private const int MaxCommoditiesWalked = 512;
    /// <summary>Nombre max d’entrées dans le DTO API (le reste est compté mais non listé).</summary>
    public const int MaxConstructionResourcesInResponse = 80;
    private const int SampleNameCount = 5;

    /// <summary>Champ racine <c>name</c> = nom de station (CAPI /market).</summary>
    public static FrontierMarketBusinessSummary? TryParse(string? body, out string? parseError)
    {
        parseError = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            parseError = "Corps /market vide";
            return null;
        }

        if (body.Length > MaxBodyChars)
        {
            parseError = "Réponse /market trop volumineuse pour analyse métier.";
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return TryParse(doc.RootElement, out parseError);
        }
        catch (Exception ex)
        {
            parseError = ex.Message;
            return null;
        }
    }

    public static FrontierMarketBusinessSummary? TryParse(JsonElement root, out string? parseError)
    {
        parseError = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            parseError = "Racine JSON attendue : objet";
            return null;
        }

        var stationName = ReadRootStationName(root);
        var marketId = ReadMarketId(root);
        var (fullList, totalCount) = ReadConstructionCommodities(root);

        var sample = fullList.Take(SampleNameCount).Select(x => x.Name).ToList();
        var capped = fullList.Take(MaxConstructionResourcesInResponse).ToList();

        return new FrontierMarketBusinessSummary(
            StationName: stationName,
            MarketId: marketId,
            HasConstructionResources: totalCount > 0,
            ConstructionResourcesCount: totalCount,
            ConstructionResourcesSample: sample,
            ConstructionResources: capped);
    }

    private static string? ReadRootStationName(JsonElement root)
    {
        if (!root.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String)
            return null;
        var s = n.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Length <= MaxCommodityNameChars ? s : s[..MaxCommodityNameChars] + "…";
    }

    /// <summary>
    /// Ordre : marketId, MarketId, market_id, puis <c>id</c> racine (CAPI /market).
    /// Nombre → chaîne invariante ; jamais d’exception.
    /// </summary>
    private static string? ReadMarketId(JsonElement root)
    {
        foreach (var key in new[] { "marketId", "MarketId", "market_id", "id" })
        {
            if (!root.TryGetProperty(key, out var el))
                continue;
            var s = TryCoerceMarketIdValue(el);
            if (!string.IsNullOrWhiteSpace(s))
                return CapMarketIdString(s);
        }

        return null;
    }

    private static string? TryCoerceMarketIdValue(JsonElement el)
    {
        try
        {
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number when el.TryGetInt64(out var n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonValueKind.Number => el.GetRawText(),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? CapMarketIdString(string s)
    {
        var t = s.Trim();
        if (t.Length == 0) return null;
        return t.Length <= 160 ? t : t[..160] + "…";
    }

    private static (IReadOnlyList<FrontierConstructionResourceItem> List, int TotalCount) ReadConstructionCommodities(JsonElement root)
    {
        JsonElement? commodities = null;
        if (TryGetPropertyIgnoreCase(root, "requiredConstructionResources", out var rcr) && rcr.ValueKind == JsonValueKind.Object)
        {
            if (TryGetPropertyIgnoreCase(rcr, "commodities", out var c))
                commodities = c;
        }

        if (commodities == null)
            return (Array.Empty<FrontierConstructionResourceItem>(), 0);

        var list = new List<FrontierConstructionResourceItem>();
        var comm = commodities.Value;

        switch (comm.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var n = 0;
                foreach (var p in comm.EnumerateObject())
                {
                    if (n >= MaxCommoditiesWalked) break;
                    var item = CommodityFromObjectProperty(p.Name, p.Value);
                    if (item != null)
                    {
                        list.Add(item);
                        n++;
                    }
                }

                break;
            }
            case JsonValueKind.Array:
            {
                var i = 0;
                foreach (var el in comm.EnumerateArray())
                {
                    if (i >= MaxCommoditiesWalked) break;
                    var item = CommodityFromArrayElement(el);
                    if (item != null)
                        list.Add(item);
                    i++;
                }

                break;
            }
        }

        var ordered = list
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (ordered, ordered.Count);
    }

    private static FrontierConstructionResourceItem? CommodityFromObjectProperty(string propertyName, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return null;
        var name = CapName(string.IsNullOrWhiteSpace(propertyName) ? "?" : propertyName);
        var req = ReadLong(value, "required", "Required");
        var prov = ReadLong(value, "provided", "Provided");
        var rem = Math.Max(0, req - prov);
        return new FrontierConstructionResourceItem(name, req, prov, rem);
    }

    private static FrontierConstructionResourceItem? CommodityFromArrayElement(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;
        string? rawName = null;
        foreach (var nk in new[] { "name", "Name", "locName", "LocName", "commodityName" })
        {
            if (el.TryGetProperty(nk, out var nm) && nm.ValueKind == JsonValueKind.String)
            {
                rawName = nm.GetString();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(rawName))
            return null;
        var name = CapName(rawName);
        var req = ReadLong(el, "required", "Required");
        var prov = ReadLong(el, "provided", "Provided");
        var rem = Math.Max(0, req - prov);
        return new FrontierConstructionResourceItem(name, req, prov, rem);
    }

    private static string CapName(string s)
    {
        if (s.Length <= MaxCommodityNameChars) return s;
        return s[..MaxCommodityNameChars] + "…";
    }

    private static long ReadLong(JsonElement obj, string a, string b)
    {
        if (obj.TryGetProperty(a, out var el)) return ToLong(el);
        if (obj.TryGetProperty(b, out el)) return ToLong(el);
        return 0;
    }

    private static long ToLong(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt64(out var l) => l,
            JsonValueKind.Number => (long)el.GetDouble(),
            _ => 0,
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
