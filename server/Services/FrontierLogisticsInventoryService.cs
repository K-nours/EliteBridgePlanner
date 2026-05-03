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

    /// <summary>
    /// Retourne le JSON brut de /fleetcarrier pour debug (noms + quantités avant normalisation).
    /// </summary>
    public async Task<string?> FetchRawFleetCarrierAsync(string accessToken, CancellationToken ct = default)
    {
        var (status, body, _) = await _auth.FetchCapiRawWithRetryAsync(accessToken, "/fleetcarrier", TimeSpan.FromSeconds(60), ct);
        _log.LogInformation("[LogisticsInventory][DEBUG] /fleetcarrier raw status={Status} len={Len}", status, body?.Length ?? 0);
        return status == 200 ? body : $"{{\"error\":\"HTTP {status}\"}}";
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
            dto.FleetCarrierSkippedDueToProfileRateLimit = false;
            _log.LogWarning("[LogisticsInventory] /profile HTTP 429 — tentative /fleetcarrier quand même, RetryAfter={Ra}s", dto.RetryAfterSeconds);
            // On continue vers /fleetcarrier même en 429 sur /profile
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
                var rootKeys = doc.RootElement.ValueKind == JsonValueKind.Object
                    ? string.Join(", ", doc.RootElement.EnumerateObject().Select(p => p.Name))
                    : "(non-objet)";
                _log.LogInformation("[LogisticsInventory] /profile root keys=[{RootKeys}]", rootKeys);
                dto.ShipCargoDebugHint = BuildCargoDebugHint(doc.RootElement);
                _log.LogInformation("[LogisticsInventory] /profile cargo hint: {Hint}", dto.ShipCargoDebugHint);
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

        // /fleetcarrier : toujours appelé, même si /profile était en 429.
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
    /// Normalise un nom de commodité CAPI (EN/FR/interne PascalCase) vers la clé canonique partagée avec le client.
    /// Les clés canoniques correspondent aux clés de COMMODITY_LABELS dans chantier-logistics.vm.ts.
    /// Couvre : noms internes CAPI ($Steel_Name; → "Steel"), noms localisés EN et FR.
    /// </summary>
    private static string NormalizeCommodityKey(string name)
    {
        var s = name.Trim();
        if (string.IsNullOrEmpty(s)) return s;
        var lower = s.ToLowerInvariant();
        return lower switch
        {
            // ── Métaux ────────────────────────────────────────────────────────
            "steel" or "acier" => "steel",
            "aluminium" or "aluminum" => "aluminium",
            "silver" or "argent" => "silver",
            "beryllium" or "béryllium" or "beryllium" => "beryllium",
            "bismuth" => "bismuth",
            "cobalt" => "cobalt",
            "copper" or "cuivre" => "copper",
            "gallium" => "gallium",
            "hafnium178" or "hafnium 178" => "hafnium178",
            "indium" => "indium",
            "lanthanum" or "lanthane" => "lanthanum",
            "lithium" => "lithium",
            "gold" or "or" => "gold",
            "osmium" => "osmium",
            "palladium" => "palladium",
            "platinum" or "platine" => "platinum",
            "praseodymium" or "praséodyme" or "praseodyme" => "praseodymium",
            "samarium" => "samarium",
            "tantalum" or "tantale" => "tantalum",
            "thallium" => "thallium",
            "thorium" => "thorium",
            "titanium" or "titane" => "titanium",
            "uranium" => "uranium",
            "lead" or "plomb" => "lead",
            "zinc" => "zinc",
            "nickel" => "nickel",
            "molybdenum" or "molybdène" or "molybdene" => "molybdenum",
            "rhenium" or "rhénium" or "rhenium" => "rhenium",
            "boron" or "bore" => "boron",
            "sulphur" or "sulfur" or "soufre" => "sulphur",
            "phosphorus" or "phosphore" => "phosphorus",
            "manganese" or "manganèse" or "manganese" => "manganese",
            "tin" or "étain" or "etain" => "tin",
            "tungsten" or "tungstène" or "wolfram" => "tungsten",
            "tellurium" or "tellure" => "tellurium",
            "vanadium" => "vanadium",
            "chromium" or "chrome" => "chromium",
            "polonium" => "polonium",
            "ruthenium" or "ruthénium" or "ruthenium" => "ruthenium",
            "technetium" or "technétium" or "technetium" => "technetium",
            "yttrium" => "yttrium",
            "antimony" or "antimoine" => "antimony",

            // ── Minéraux ──────────────────────────────────────────────────────
            "alexandrite" => "alexandrite",
            "bauxite" => "bauxite",
            "benitoite" or "bénitoïte" or "benitoite" => "benitoite",
            "bertrandite" => "bertrandite",
            "bromellite" => "bromellite",
            "siliconcarbidefibres" or "silicon carbide fibres" or "carbure de silicium" => "siliconcarbidefibres",
            "coltan" => "coltan",
            "methanolmonohydratecrystals" or "methanol monohydrate crystals"
                or "cristaux de méthanol monohydraté" or "cristaux de methanol monohydrate" => "methanolmonohydratecrystals",
            "cryolite" => "cryolite",
            "lowtemperaturediamond" or "low temperature diamonds" or "low temperature diamond"
                or "diamants basse température" or "diamants basse temperature" => "lowtemperaturediamond",
            "gallite" => "gallite",
            "goslarite" => "goslarite",
            "grandidierite" or "grandidiérite" or "grandidierite" => "grandidierite",
            "hematite" or "hématite" or "hematite" => "hematite",
            "methanehydrate" or "methane hydrate" or "hydrate de méthane" or "hydrate de methane" => "methanehydrate",
            "lithiumhydroxide" or "lithium hydroxide" or "hydroxyde de lithium" => "lithiumhydroxide",
            "indite" => "indite",
            "jadeite" or "jadéite" or "jadeite" => "jadeite",
            "lepidolite" or "lépidolite" or "lepidolite" => "lepidolite",
            "monazite" => "monazite",
            "musgravite" => "musgravite",
            "voidopal" or "void opal" or "opale du vide" => "voidopal",
            "painite" => "painite",
            "pyrophyllite" => "pyrophyllite",
            "rhodplumsite" => "rhodplumsite",
            "rutile" => "rutile",
            "serendibite" => "serendibite",
            "taaffeite" or "taafféite" or "taaffeite" => "taaffeite",
            "uraninite" => "uraninite",

            // ── Matériaux industriels ─────────────────────────────────────────
            "ceramiccomposites" or "ceramic composites"
                or "composites céramiques" or "composites ceramiques"
                or "composés en céramique" or "composes en ceramique"
                or "composés céramiques" or "composes ceramiques" => "ceramiccomposites",
            "polymers" or "polymères" or "polymeres" or "polymère(s)" or "polymere(s)" => "polymers",
            "semiconductors" or "semi-conducteurs" or "semiconducteurs" or "semi-conducteur(s)" => "semiconductors",
            "superconductors" or "supraconducteurs" or "supraconducteur(s)" => "superconductors",
            "cmmcomposite" or "cmm composite" or "composite mmc" => "cmmcomposite",
            "insulatingmembrane" or "insulating membrane" or "membrane isolante" => "insulatingmembrane",
            "neofabricinsulation" or "neofabric insulation"
                or "isolant en néotextile" or "isolant en neotextile" => "neofabricinsulation",
            "metaalloys" or "meta-alloys" or "méta-alliages" or "meta-alliages" => "metaalloys",
            "coolinghoses" or "cooling hoses" or "tuyaux de refroidissement" => "coolinghoses",
            "reactivearmour" or "reactive armour" or "reactive armor"
                or "armure réactive" or "armure reactive" => "reactivearmour",
            "reactivearmouring" or "reactive armouring"
                or "protection réactive" or "protection reactive" => "reactivearmouring",

            // ── Machines ──────────────────────────────────────────────────────
            "autofabricators" or "auto-fabricators" or "auto fabricators" or "auto-bâtisseurs" or "auto-batisseurs" => "autofabricators",
            "buildingfabricators" or "building fabricators"
                or "fabricants de bâtiments" or "fabricants de batiments" => "buildingfabricators",
            "magneticemittercoil" or "magnetic emitter coil"
                or "bobine d'émission magnétique" or "bobine d emission magnetique" => "magneticemittercoil",
            "emergencypowercells" or "emergency power cells"
                or "cellules d'énergie de secours" or "cellules d energie de secours" => "emergencypowercells",
            "exhaustmanifold" or "exhaust manifold"
                or "collecteur d'échappement" or "collecteur d echappement" => "exhaustmanifold",
            "shieldemitters" or "shield emitters" or "composants de protecteurs" => "shieldemitters",
            "energygridassembly" or "energy grid assembly"
                or "conduits de transfert d'énergie" or "conduits de transfert d energie" => "energygridassembly",
            "powerconverter" or "power converter"
                or "convertisseur d'énergie" or "convertisseur d energie" => "powerconverter",
            "iondistributor" or "ion distributor"
                or "distributeurs d'ions" or "distributeurs d ions" => "iondistributor",
            "radiationbaffle" or "radiation baffle" or "écran antiradiation" or "ecran antiradiation" => "radiationbaffle",
            "marinesupplies" or "marine supplies" or "équipement aquamarin" or "equipement aquamarin" => "marinesupplies",
            "geologicalequipment" or "geological equipment"
                or "équipement géologique" or "equipement geologique" => "geologicalequipment",
            "mineralextractors" or "mineral extractors" or "extracteurs de minerai" => "mineralextractors",
            "powergenerators" or "power generators" or "générateurs" or "generateurs" => "powergenerators",
            "microbialfurnaces" or "microbial furnaces" or "hauts fourneaux microbiens" => "microbialfurnaces",
            "thermalcoolingunits" or "thermal cooling units"
                or "interconnexion dissipateur therm." or "interconnexion dissipateur thermique" => "thermalcoolingunits",
            "cropharvesters" or "crop harvesters" or "moissonneuses" => "cropharvesters",
            "articulationmotors" or "articulation motors"
                or "moteurs à articulation" or "moteurs a articulation" => "articulationmotors",
            "reinforcedmountingplate" or "reinforced mounting plate"
                or "plaque de montage renforcée" or "plaque de montage renforcee" => "reinforcedmountingplate",
            "atmosphericprocessors" or "atmospheric processors"
                or "processeurs atmosphériques" or "processeurs atmospheriques" => "atmosphericprocessors",
            "hndshockmount" or "hn shock mount" or "protection antichocs hp" => "hndshockmount",
            "powergridassembly" or "power grid assembly"
                or "système de réseau d'alimentation" or "systeme de reseau d alimentation" => "powergridassembly",
            "modularterminals" or "modular terminals" or "terminaux modulaires" => "modularterminals",
            "coolingunits" or "cooling units" or "unités de refroidissement" or "unites de refroidissement" => "coolingunits",
            "waterpurifiers" or "water purifiers"
                or "purificateurs d'eau" or "purificateurs d eau" => "waterpurifiers",
            "liquidoxygen" or "liquid oxygen" or "oxygène liquide" or "oxygene liquide" => "liquidoxygen",
            "surfacestabilisers" or "surface stabilisers" or "stabilisateurs de surface" => "surfacestabilisers",
            "structuralregulators" or "structural regulators"
                or "régulateurs structurels" or "regulateurs structurels" => "structuralregulators",
            "mutomimager" or "muon tomography imager"
                or "dispositif d'imagerie muonique" or "dispositif d imagerie muonique" => "mutomimager",
            "computercomponents" or "computer components"
                or "composants d'ordinateur" or "composants d ordinateur" => "computercomponents",
            "landenrichmentsystems" or "land enrichment systems" or "landenviromentalsystems"
                or "systèmes d'enrichissement" or "systemes d enrichissement"
                or "sys. enrichissement sols" => "landenrichmentsystems",
            "animalmonitor" or "animal monitor" or "animalnmonitor"
                or "sys. surveillance animale" or "système de surveillance animale" => "animalmonitor",
            "telemetrysuite" or "telemetry suite"
                or "système de télémétrie" or "systeme de telemetrie" => "telemetrysuite",
            "aquaponicssystems" or "aquaponics systems"
                or "systèmes aquaponiques" or "systemes aquaponiques" => "aquaponicssystems",
            "bioreducinglichen" or "bio-reducing lichen"
                or "lichen bioréducteur" or "lichen bioreducteur" => "bioreducinglichen",
            "microcontrollers" or "microcontrôleurs" or "microcontroleurs" => "microcontrollers",
            "nanodestructors" or "nanodestructeurs" => "nanodestructors",
            "robotics" or "robots" => "robotics",
            "resonatingseparators" or "resonating separators"
                or "séparateurs à résonance" or "separateurs a resonance" => "resonatingseparators",
            "complexcatalysts" or "complex catalysts" or "advanced catalysers" or "catalyseurs complexes" => "complexcatalysts",
            "diagnosticssensor" or "diagnostics sensor"
                or "capteur diagnostic d'équipement" or "capteur diagnostic d equipement" => "diagnosticssensor",
            "medicaldiagnosticequipment" or "medical diagnostic equipment"
                or "équipement de diagnostic médical" or "equipement de diagnostic medical" => "medicaldiagnosticequipment",

            // ── Médicaments ───────────────────────────────────────────────────
            "agriculturalmedicines" or "agricultural medicines"
                or "agri-médicaments" or "agri-medicaments" => "agriculturalmedicines",
            "progenitorcells" or "progenitor cells" or "cellules souches" => "progenitorcells",
            "basicmedicines" or "basic medicines" or "médicaments simples" or "medicaments simples" => "basicmedicines",
            "advancedmedicines" or "advanced medicines" or "médicaments complexes" or "medicaments complexes" => "advancedmedicines",
            "performanceenhancers" or "performance enhancers" or "produits dopants" => "performanceenhancers",
            "combatantimutagens" or "combat antimutagens" or "stabilisateurs de combat" => "combatantimutagens",
            "combatstabilisers" or "combat stabilisers" or "combat stabilizers" => "combatstabilisers",

            // ── Nourriture ────────────────────────────────────────────────────
            "algae" or "algues" => "algae",
            "coffee" or "café" or "cafe" => "coffee",
            "foodcartridges" or "food cartridges" or "cartouches alimentaires"
                or "cartouche(s) alimentaire(s)" or "cartouche(s) alimentaires" => "foodcartridges",
            "grain" or "céréales" or "cereales" => "grain",
            "fruitandvegetables" or "fruit and vegetables"
                or "fruits et légumes" or "fruits et legumes" => "fruitandvegetables",
            "fish" or "poisson" => "fish",
            "tea" or "thé" or "the" => "tea",
            "meat" or "viande" => "meat",
            "animalmeat" or "animal meat" or "viande animale" => "animalmeat",
            "syntheticmeat" or "synthetic meat" or "viande synthétique" or "viande synthetique" => "syntheticmeat",

            // ── Produits chimiques ────────────────────────────────────────────
            "nerveagents" or "nerve agents" or "agents neurotoxiques" => "nerveagents",
            "hydrogenfuel" or "hydrogen fuel"
                or "carburant à base d'hydrogène" or "carburant a base d hydrogene" => "hydrogenfuel",
            "water" or "eau" => "water",
            "rockforthfertiliser" or "rockforth fertiliser" or "rockforth fertilizer"
                or "engrais rockforth" => "rockforthfertiliser",
            "explosives" or "explosifs" => "explosives",
            "mineraloil" or "mineral oil" or "huiles minérales" or "huiles minerales" => "mineraloil",
            "hydrogenperoxide" or "hydrogen peroxide"
                or "peroxyde d'hydrogène" or "peroxyde d hydrogene" => "hydrogenperoxide",
            "pesticides" => "pesticides",
            "syntheticreagents" or "synthetic reagents"
                or "réactifs synthétiques" or "reactifs synthetiques" => "syntheticreagents",
            "agronomictreatment" or "agronomic treatment" or "traitement agronomique" => "agronomictreatment",
            "tritium" => "tritium",

            // ── Produits de consommation ──────────────────────────────────────
            "consumertechnology" or "consumer technology"
                or "électronique grand public" or "electronique grand public" => "consumertechnology",
            "survivalequipment" or "survival equipment"
                or "équipement de survie" or "equipement de survie" => "survivalequipment",
            "domesticappliances" or "domestic appliances"
                or "équipement ménager" or "equipement menager" => "domesticappliances",
            "evacuationshelter" or "evacuation shelter"
                or "abri d'urgence" or "abri d urgence" => "evacuationshelter",
            "hazardousenvironmentsuits" or "hazardous environment suits" or "hazard environment suits"
                or "combinaisons de protection" => "hazardousenvironmentsuits",
            "hazardenvironmentsuits" => "hazardenvironmentsuits",
            "clothing" or "vêtements" or "vetements" => "clothing",

            // ── Drogues légales ───────────────────────────────────────────────
            "beer" or "bière" or "biere" => "beer",
            "bootlegliquor" or "bootleg liquor" or "liqueur de contrebande" => "bootlegliquor",
            "narcotics" or "narcotiques" => "narcotics",
            "liquor" or "spiritueux" => "liquor",
            "tobacco" or "tabac" => "tobacco",
            "onionheadgammastrain" or "onion head gamma strain"
                or "variété gamma de tête d'oignon" or "variete gamma de tete d oignon" => "onionheadgammastrain",
            "wine" or "vin" => "wine",

            // ── Textiles ──────────────────────────────────────────────────────
            "leather" or "cuir" => "leather",
            "naturalfabrics" or "natural fabrics" or "fibres textiles naturelles" => "naturalfabrics",
            "syntheticfabrics" or "synthetic fabrics"
                or "tissus synthétiques" or "tissus synthetiques" => "syntheticfabrics",
            "conductivefabrics" or "conductive fabrics" or "tissus conducteurs" => "conductivefabrics",
            "militarygradefabrics" or "military grade fabrics" or "tissus militaires" => "militarygradefabrics",

            // ── Déchets ───────────────────────────────────────────────────────
            "biowaste" or "biodéchets" or "biodechets" => "biowaste",
            "chemicalwaste" or "chemical waste" or "déchets chimiques" or "dechets chimiques" => "chemicalwaste",
            "toxicwaste" or "toxic waste" or "déchets toxiques" or "dechets toxiques" => "toxicwaste",
            "scrap" or "ferraille" => "scrap",

            // ── Armes ─────────────────────────────────────────────────────────
            "personalweapons" or "personal weapons" or "armes de poing" => "personalweapons",
            "nonlethalweapons" or "non-lethal weapons" or "non lethal weapons" or "armes incapacitantes" => "nonlethalweapons",
            "battleweapons" or "battle weapons" or "armes militaires" => "battleweapons",
            "landmines" or "land mines" or "mines terrestres" => "landmines",

            // ── Esclaves ──────────────────────────────────────────────────────
            "slaves" or "esclaves" => "slaves",
            "imperialslaves" or "imperial slaves" or "esclaves impériaux" or "esclaves imperiaux" => "imperialslaves",

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

            var normalizedKey = NormalizeCommodityKey(name);
            log?.LogDebug("[LogisticsInventory][RAW] rawName={Raw} normalizedKey={Key} qty={Qty}",
                name, normalizedKey, qty);

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
