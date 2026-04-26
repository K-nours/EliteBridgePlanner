using System.Text.Json;
using System.Text.RegularExpressions;
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

        var (shipStatus, shipBody, shipRetryAfter) =
            await _auth.FetchCapiRawWithRetryAsync(accessToken, "/profile", TimeSpan.FromSeconds(15), ct);

        if (shipStatus == 429)
        {
            dto.ShipRateLimited = true;
            dto.RateLimited = true;
            dto.RetryAfterSeconds = shipRetryAfter ?? 60;
            dto.ShipCargoError = "Profil Frontier (rate limit)";
            dto.FleetCarrierSkippedDueToProfileRateLimit = true;
            _log.LogWarning("[LogisticsInventory] /profile HTTP 429 — skip /fleetcarrier, RetryAfter={Ra}s", dto.RetryAfterSeconds);
            return dto;
        }

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
                dto.ShipCargoDebugHint = BuildCargoDebugHint(doc.RootElement);
                MergeCargoArraysFromRoot(doc.RootElement, dto.ShipCargoByName, _log);
                _log.LogInformation("[LogisticsInventory] ship cargo keys={Count} items=[{Keys}]",
                    dto.ShipCargoByName.Count,
                    string.Join(", ", dto.ShipCargoByName.Keys));
            }
            catch (Exception ex)
            {
                dto.ShipCargoError = "Profil illisible";
                _log.LogWarning(ex, "[LogisticsInventory] parse /profile");
            }
        }

        // /fleetcarrier : seulement si /profile n’a pas été limité (évite 2 hits CAPI inutiles).
        var (fcStatus, fcBody, fcRetryAfter) =
            await _auth.FetchCapiRawWithRetryAsync(accessToken, "/fleetcarrier", TimeSpan.FromSeconds(60), ct);
        if (fcStatus == 404)
        {
            dto.CarrierCargoError = null;
        }
        else if (fcStatus == 429)
        {
            dto.CarrierRateLimited = true;
            dto.RateLimited = true;
            dto.RetryAfterSeconds = fcRetryAfter ?? dto.RetryAfterSeconds ?? 60;
            dto.CarrierCargoError = "Fleet Carrier (rate limit)";
            _log.LogWarning("[LogisticsInventory] /fleetcarrier HTTP 429 RetryAfter={Ra}s", dto.RetryAfterSeconds);
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
                MergeCargoArraysFromRoot(doc.RootElement, dto.CarrierCargoByName, _log);
                _log.LogInformation("[LogisticsInventory] FC cargo keys={Count} items=[{Keys}]",
                    dto.CarrierCargoByName.Count,
                    string.Join(", ", dto.CarrierCargoByName.Keys));
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
    private static void MergeCargoArraysFromRoot(JsonElement root, Dictionary<string, int> dict, ILogger? log = null)
    {
        WalkElement(root, dict, depth: 0, log);
    }

    /// <summary>
    /// Aligné sur la logique client (chantier-logistics.vm) : FR/EN et casse unique pour lookup inventaire.
    /// </summary>
    private static string NormalizeCommodityKey(string name)
    {
        var s = name.Trim();
        if (string.IsNullOrEmpty(s)) return s;
        var lower = s.ToLowerInvariant();
        return lower switch
        {
            "acier" or "steel" => "steel",
            "aluminium" or "aluminum" => "aluminium",
            "cuivre" or "copper" => "copper",
            "titane" or "titanium" => "titanium",
            "plomb" or "lead" => "lead",
            "zinc" => "zinc",
            "nickel" => "nickel",
            "cobalt" => "cobalt",
            "soufre" or "sulphur" or "sulfur" => "sulphur",
            "phosphore" or "phosphorus" => "phosphorus",
            "bore" or "boron" => "boron",
            "tellure" or "tellurium" => "tellurium",
            "chrome" or "chromium" => "chromium",
            "antimoine" or "antimony" => "antimony",
            "étain" or "etain" or "tin" => "tin",
            "manganèse" or "manganese" => "manganese",
            "molybdène" or "molybdene" or "molybdenum" => "molybdenum",
            "rhénium" or "rhenium" => "rhenium",
            "wolfram" or "tungstène" or "tungsten" => "tungsten",
            "yttrium" => "yttrium",
            "technétium" or "technetium" => "technetium",
            "ruthénium" or "ruthenium" => "ruthenium",
            "polonium" => "polonium",
            "vanadium" => "vanadium",
            "cmmcomposite" or "cmm composite" or "composite mmc" => "cmmcomposite",
            "liquidoxygen" or "liquid oxygen" or "oxygène liquide" or "oxygene liquide" => "liquidoxygen",
            "ceramiccomposites" or "ceramic composites" or "composites céramiques" or "composites ceramiques" => "ceramiccomposites",
            "polymers" or "polymères" or "polymeres" => "polymers",
            "semiconductors" or "semi-conducteurs" or "semiconducteurs" => "semiconductors",
            "superconductors" or "supraconducteurs" => "superconductors",
            "buildingfabricators" or "building fabricators" or "fabricants de bâtiments" or "fabricants de batiments" => "buildingfabricators",
            "insulatingmembrane" or "insulating membrane" or "membrane isolante" => "insulatingmembrane",
            "reactivearmour" or "reactive armour" or "reactive armor" or "armure réactive" or "armure reactive" => "reactivearmour",
            "landenviromentalsystems" or "land enrichment systems" or "systèmes d'enrichissement" => "landenviromentalsystems",
            _ => Regex.Replace(lower, @"\s+", " ")
        };
    }

    private static void AddOrMerge(Dictionary<string, int> dict, string name, int qty)
    {
        var key = NormalizeCommodityKey(name);
        if (string.IsNullOrEmpty(key)) return;
        string? existingKey = null;
        foreach (var kv in dict)
        {
            if (string.Equals(NormalizeCommodityKey(kv.Key), key, StringComparison.Ordinal))
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

    private static void WalkElement(JsonElement el, Dictionary<string, int> dict, int depth, ILogger? log = null)
    {
        if (depth > 24) return;

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    if (p.Name.Equals("cargo", StringComparison.OrdinalIgnoreCase))
                    {
                        if (p.Value.ValueKind == JsonValueKind.Array)
                        {
                            // /fleetcarrier : "cargo" est directement un tableau
                            MergeCargoArray(p.Value, dict, log);
                        }
                        else if (p.Value.ValueKind == JsonValueKind.Object)
                        {
                            // /profile : "cargo" est un objet { capacity, qty, items: [...|{}], stolen: [...] }
                            bool foundItems = false;
                            foreach (var inner in p.Value.EnumerateObject())
                            {
                                if (!inner.Name.Equals("items", StringComparison.OrdinalIgnoreCase)) continue;
                                if (inner.Value.ValueKind == JsonValueKind.Array)
                                {
                                    MergeCargoArray(inner.Value, dict, log);
                                    foundItems = true;
                                }
                                else if (inner.Value.ValueKind == JsonValueKind.Object)
                                {
                                    // items est un dictionnaire : { "polymers": { locName, qty, ... }, ... }
                                    MergeCargoObjectDict(inner.Value, dict, log);
                                    foundItems = true;
                                }
                            }
                            if (!foundItems)
                            {
                                // Structure inconnue — walk récursif pour ne rien rater
                                WalkElement(p.Value, dict, depth + 1, log);
                            }
                        }
                    }
                    else
                    {
                        WalkElement(p.Value, dict, depth + 1, log);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    WalkElement(item, dict, depth + 1, log);
                break;
        }
    }

    private static void MergeCargoObjectDict(JsonElement itemsObject, Dictionary<string, int> dict, ILogger? log = null)
    {
        foreach (var prop in itemsObject.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            var name = ExtractCommodityName(prop.Value);
            if (string.IsNullOrWhiteSpace(name)) name = prop.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var qty = ExtractQuantity(prop.Value);
            if (qty <= 0) qty = 1;
            AddOrMerge(dict, name, qty);
        }
    }

    private static void MergeCargoArray(JsonElement cargoArray, Dictionary<string, int> dict, ILogger? log = null)
    {
        foreach (var item in cargoArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = ExtractCommodityName(item);
            if (string.IsNullOrWhiteSpace(name))
            {
                log?.LogWarning("[LogisticsInventory] item sans nom reconnu — JSON brut : {Raw}", item.GetRawText());
                continue;
            }

            var qty = ExtractQuantity(item);
            if (qty <= 0)
                qty = 1;

            AddOrMerge(dict, name, qty);
        }
    }

    /// <summary>
    /// CAPI renvoie les noms de commodités sous la forme <c>$Robotics_Name;</c> dans le champ <c>name</c>.
    /// Cette méthode extrait le nom canonique : <c>$Robotics_Name;</c> → <c>Robotics</c>.
    /// Retourne null si le format ne correspond pas.
    /// </summary>
    private static string? ParseCapiInternalName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        // Format : $CommodityName_Name;  (insensible à la casse pour le suffixe)
        if (t.StartsWith('$') && t.EndsWith("_name;", StringComparison.OrdinalIgnoreCase) && t.Length > 7)
            return t[1..^6]; // retire '$' en tête et '_Name;' en queue
        return null;
    }

    private static string? ExtractCommodityName(JsonElement item)
    {
        foreach (var nk in new[] { "name", "Name", "locName", "LocName", "localizedName", "LocalizedName", "title", "Title" })
        {
            if (!item.TryGetProperty(nk, out var n) || n.ValueKind != JsonValueKind.String) continue;
            var s = n.GetString();
            if (string.IsNullOrWhiteSpace(s)) continue;
            // Priorité : si c'est un nom CAPI interne ($Robotics_Name;), extraire le nom propre.
            // Sinon retourner la valeur brute (nom localisé ou clé directe).
            return ParseCapiInternalName(s) ?? s;
        }

        // "commodity" peut être une string directe (ex. "liquidoxygen") ou un objet
        if (item.TryGetProperty("commodity", out var comm))
        {
            if (comm.ValueKind == JsonValueKind.String)
            {
                var s = comm.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return ParseCapiInternalName(s) ?? s;
            }
            else if (comm.ValueKind == JsonValueKind.Object)
            {
                foreach (var nk in new[] { "name", "Name", "locName", "LocName", "localizedName", "LocalizedName" })
                {
                    if (!comm.TryGetProperty(nk, out var n) || n.ValueKind != JsonValueKind.String) continue;
                    var s = n.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return ParseCapiInternalName(s) ?? s;
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

    /// <summary>
    /// [DEBUG temporaire] Cherche le premier bloc "cargo" dans le JSON /profile
    /// et retourne une description compacte de sa structure.
    /// </summary>
    private static string BuildCargoDebugHint(JsonElement root)
    {
        return FindCargoHint(root, depth: 0, path: "root") ?? "cargo: introuvable dans le JSON";
    }

    private static string? FindCargoHint(JsonElement el, int depth, string path)
    {
        if (depth > 8) return null;
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
            {
                var childPath = $"{path}.{p.Name}";
                if (p.Name.Equals("cargo", StringComparison.OrdinalIgnoreCase))
                {
                    if (p.Value.ValueKind == JsonValueKind.Object)
                    {
                        var subKeys = p.Value.EnumerateObject()
                            .Select(inner =>
                            {
                                if (inner.Value.ValueKind == JsonValueKind.Array)
                                    return $"{inner.Name}(array[{inner.Value.GetArrayLength()}])";
                                if (inner.Value.ValueKind == JsonValueKind.Object)
                                {
                                    var keys = inner.Value.EnumerateObject().Take(3).Select(k => k.Name);
                                    return $"{inner.Name}(obj[{string.Join(",", keys)}])";
                                }
                                var raw = inner.Value.GetRawText();
                                return $"{inner.Name}={raw[..Math.Min(40, raw.Length)]}";
                            });
                        return $"path={childPath} kind=Object subKeys=[{string.Join(" | ", subKeys)}]";
                    }
                    if (p.Value.ValueKind == JsonValueKind.Array)
                    {
                        var first = p.Value.EnumerateArray().Take(1).Select(x => x.GetRawText()[..Math.Min(120, x.GetRawText().Length)]).FirstOrDefault() ?? "(vide)";
                        return $"path={childPath} kind=Array len={p.Value.GetArrayLength()} first={first}";
                    }
                    return $"path={childPath} kind={p.Value.ValueKind} raw={p.Value.GetRawText()[..Math.Min(80, p.Value.GetRawText().Length)]}";
                }
                var found = FindCargoHint(p.Value, depth + 1, childPath);
                if (found != null) return found;
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in el.EnumerateArray())
            {
                var found = FindCargoHint(item, depth + 1, $"{path}[{i++}]");
                if (found != null) return found;
                if (i > 2) break;
            }
        }
        return null;
    }
}
