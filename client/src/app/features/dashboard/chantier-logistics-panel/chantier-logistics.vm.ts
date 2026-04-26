import type { ChantierLogisticsInventoryDto } from '../../../core/models/chantier-logistics-inventory.model';
import type {
  ActiveChantierSite,
  ConstructionResourceSnapshot,
} from '../../../core/state/active-chantiers.store';

export type ResourceAvailabilityStatus = 'ok' | 'warn' | 'zero';

/** Indique si les quantités côté CAPI sont exploitables (sinon afficher « — »). */
export interface InventoryTrust {
  shipKnown: boolean;
  carrierKnown: boolean;
}

export interface ChantierResourceRowVm {
  name: string;
  /** Libellé localisé (FR ou EN) — fallback sur `name` brut si inconnu. */
  displayName: string;
  need: number;
  /** Somme des besoins restants pour cette marchandise, tous chantiers actifs « mine » du commandant (clé unique chantier). */
  globalNeed: number;
  shipQty: number;
  carrierQty: number;
  status: ResourceAvailabilityStatus;
}

function normalizeCommodityKey(s: string): string {
  return s.trim().toLowerCase().replace(/\s+/g, ' ');
}

/**
 * Groupes de synonymes (FR/EN / orthographe) → même commodité.
 * La clé canonique est le 1er terme du groupe (normalisé).
 */
const COMMODITY_EQUIVALENCE_GROUPS: readonly (readonly string[])[] = [
  ['steel', 'acier'],
  ['aluminium', 'aluminum'],
  ['copper', 'cuivre'],
  ['titanium', 'titane'],
  ['lead', 'plomb'],
  ['zinc'],
  ['nickel'],
  ['cobalt'],
  ['molybdenum', 'molybdène', 'molybdene'],
  ['rhenium', 'rhénium'],
  ['boron', 'bore'],
  ['sulphur', 'sulfur', 'soufre'],
  ['phosphorus', 'phosphore'],
  ['manganese', 'manganèse'],
  ['tin', 'étain', 'etain'],
  ['tungsten', 'wolfram', 'tungstène'],
  ['tellurium', 'tellure'],
  ['vanadium'],
  ['chromium', 'chrome'],
  ['polonium'],
  ['ruthenium', 'ruthénium'],
  ['technetium', 'technétium'],
  ['yttrium'],
  ['antimony', 'antimoine'],
  ['CMMComposite', 'CMM Composite', 'composite mmc', 'cmmcomposite'],
  ['LiquidOxygen', 'Liquid Oxygen', 'oxygène liquide', 'oxygene liquide', 'liquidoxygen'],
  ['CeramicComposites', 'Ceramic Composites', 'composites céramiques', 'composites ceramiques', 'ceramiccomposites'],
  ['Polymers', 'polymères', 'polymeres'],
  ['Semiconductors', 'semi-conducteurs', 'semiconducteurs'],
  ['Superconductors', 'supraconducteurs'],
  ['BuildingFabricators', 'Building Fabricators', 'fabricants de bâtiments', 'fabricants de batiments', 'buildingfabricators'],
  ['InsulatingMembrane', 'Insulating Membrane', 'membrane isolante', 'insulatingmembrane'],
  ['ReactiveArmour', 'Reactive Armour', 'Reactive Armor', 'armure réactive', 'armure reactive', 'reactivearmour'],
];

function buildCanonicalLookup(): Map<string, string> {
  const m = new Map<string, string>();
  for (const group of COMMODITY_EQUIVALENCE_GROUPS) {
    const canonical = normalizeCommodityKey(group[0]);
    for (const term of group) {
      m.set(normalizeCommodityKey(term), canonical);
    }
  }
  return m;
}

const CANONICAL_LOOKUP = buildCanonicalLookup();

/**
 * Labels d'affichage FR / EN par clé canonique.
 * Clé = résultat de `canonicalCommodityKey(name)` (lowercase, sans espace superflus).
 * Fallback : si la ressource est absente, on affiche le nom brut CAPI.
 */
const COMMODITY_LABELS: Readonly<Record<string, { fr: string; en: string }>> = {
  // ── Métaux ────────────────────────────────────────────────────────────────
  steel:            { fr: 'Acier',              en: 'Steel' },
  aluminium:        { fr: 'Aluminium',           en: 'Aluminium' },
  silver:           { fr: 'Argent',             en: 'Silver' },
  beryllium:        { fr: 'Béryllium',          en: 'Beryllium' },
  bismuth:          { fr: 'Bismuth',            en: 'Bismuth' },
  cobalt:           { fr: 'Cobalt',             en: 'Cobalt' },
  copper:           { fr: 'Cuivre',             en: 'Copper' },
  gallium:          { fr: 'Gallium',            en: 'Gallium' },
  hafnium178:       { fr: 'Hafnium 178',        en: 'Hafnium 178' },
  indium:           { fr: 'Indium',             en: 'Indium' },
  lanthanum:        { fr: 'Lanthane',           en: 'Lanthanum' },
  lithium:          { fr: 'Lithium',            en: 'Lithium' },
  gold:             { fr: 'Or',                 en: 'Gold' },
  osmium:           { fr: 'Osmium',             en: 'Osmium' },
  palladium:        { fr: 'Palladium',          en: 'Palladium' },
  platinum:         { fr: 'Platine',            en: 'Platinum' },
  praseodymium:     { fr: 'Praséodyme',         en: 'Praseodymium' },
  samarium:         { fr: 'Samarium',           en: 'Samarium' },
  tantalum:         { fr: 'Tantale',            en: 'Tantalum' },
  thallium:         { fr: 'Thallium',           en: 'Thallium' },
  thorium:          { fr: 'Thorium',            en: 'Thorium' },
  titanium:         { fr: 'Titane',             en: 'Titanium' },
  uranium:          { fr: 'Uranium',            en: 'Uranium' },
  lead:             { fr: 'Plomb',              en: 'Lead' },
  zinc:             { fr: 'Zinc',               en: 'Zinc' },
  nickel:           { fr: 'Nickel',             en: 'Nickel' },
  molybdenum:       { fr: 'Molybdène',          en: 'Molybdenum' },
  rhenium:          { fr: 'Rhénium',            en: 'Rhenium' },
  boron:            { fr: 'Bore',               en: 'Boron' },
  sulphur:          { fr: 'Soufre',             en: 'Sulphur' },
  phosphorus:       { fr: 'Phosphore',          en: 'Phosphorus' },
  manganese:        { fr: 'Manganèse',          en: 'Manganese' },
  tin:              { fr: 'Étain',              en: 'Tin' },
  tungsten:         { fr: 'Tungstène',          en: 'Tungsten' },
  tellurium:        { fr: 'Tellure',            en: 'Tellurium' },
  vanadium:         { fr: 'Vanadium',           en: 'Vanadium' },
  chromium:         { fr: 'Chrome',             en: 'Chromium' },
  polonium:         { fr: 'Polonium',           en: 'Polonium' },
  ruthenium:        { fr: 'Ruthénium',          en: 'Ruthenium' },
  technetium:       { fr: 'Technétium',         en: 'Technetium' },
  yttrium:          { fr: 'Yttrium',            en: 'Yttrium' },
  antimony:         { fr: 'Antimoine',          en: 'Antimony' },

  // ── Minéraux ──────────────────────────────────────────────────────────────
  alexandrite:               { fr: 'Alexandrite',                     en: 'Alexandrite' },
  bauxite:                   { fr: 'Bauxite',                         en: 'Bauxite' },
  benitoite:                 { fr: 'Bénitoïte',                       en: 'Benitoite' },
  bertrandite:               { fr: 'Bertrandite',                     en: 'Bertrandite' },
  bromellite:                { fr: 'Bromellite',                      en: 'Bromellite' },
  siliconcarbidefibres:      { fr: 'Carbure de silicium',             en: 'Silicon Carbide Fibres' },
  coltan:                    { fr: 'Coltan',                          en: 'Coltan' },
  methanolmonohydratecrystals: { fr: 'Cristaux de méthanol monohydraté', en: 'Methanol Monohydrate Crystals' },
  cryolite:                  { fr: 'Cryolite',                        en: 'Cryolite' },
  lowtemperaturediamond:     { fr: 'Diamants basse température',      en: 'Low Temperature Diamonds' },
  gallite:                   { fr: 'Gallite',                         en: 'Gallite' },
  goslarite:                 { fr: 'Goslarite',                       en: 'Goslarite' },
  grandidierite:             { fr: 'Grandidiérite',                   en: 'Grandidierite' },
  hematite:                  { fr: 'Hématite',                        en: 'Hematite' },
  methanehydrate:            { fr: 'Hydrate de méthane',              en: 'Methane Hydrate' },
  lithiumhydroxide:          { fr: 'Hydroxyde de lithium',            en: 'Lithium Hydroxide' },
  indite:                    { fr: 'Indite',                          en: 'Indite' },
  jadeite:                   { fr: 'Jadéite',                         en: 'Jadeite' },
  lepidolite:                { fr: 'Lépidolite',                      en: 'Lepidolite' },
  monazite:                  { fr: 'Monazite',                        en: 'Monazite' },
  musgravite:                { fr: 'Musgravite',                      en: 'Musgravite' },
  voidopal:                  { fr: 'Opale du vide',                   en: 'Void Opal' },
  painite:                   { fr: 'Painite',                         en: 'Painite' },
  pyrophyllite:              { fr: 'Pyrophyllite',                    en: 'Pyrophyllite' },
  rhodplumsite:              { fr: 'Rhodplumsite',                    en: 'Rhodplumsite' },
  rutile:                    { fr: 'Rutile',                          en: 'Rutile' },
  serendibite:               { fr: 'Serendibite',                     en: 'Serendibite' },
  taaffeite:                 { fr: 'Taafféite',                       en: 'Taaffeite' },
  uraninite:                 { fr: 'Uraninite',                       en: 'Uraninite' },

  // ── Matériaux industriels ─────────────────────────────────────────────────
  polymers:                  { fr: 'Polymères',              en: 'Polymers' },
  semiconductors:            { fr: 'Semi-conducteurs',       en: 'Semiconductors' },
  superconductors:           { fr: 'Supraconducteurs',       en: 'Superconductors' },
  ceramiccomposites:         { fr: 'Composés en céramique',  en: 'Ceramic Composites' },
  cmmcomposite:              { fr: 'Composite MMC',          en: 'CMM Composite' },
  insulatingmembrane:        { fr: 'Membrane isolante',      en: 'Insulating Membrane' },
  neofabricinsulation:       { fr: 'Isolant en néotextile',  en: 'Neofabric Insulation' },
  metaalloys:                { fr: 'Méta-alliages',          en: 'Meta-Alloys' },
  coolinghoses:              { fr: 'Tuyaux de refroidissement', en: 'Cooling Hoses' },
  reactivearmour:            { fr: 'Armure réactive',        en: 'Reactive Armour' },
  reactivearmouring:         { fr: 'Protection réactive',    en: 'Reactive Armouring' },

  // ── Machines ──────────────────────────────────────────────────────────────
  autofabricators:           { fr: 'Auto-bâtisseurs',                    en: 'Auto-Fabricators' },
  buildingfabricators:       { fr: 'Auto-bâtisseurs',                    en: 'Building Fabricators' },
  magneticemittercoil:       { fr: "Bobine d'émission magnétique",       en: 'Magnetic Emitter Coil' },
  emergencypowercells:       { fr: "Cellules d'énergie de secours",      en: 'Emergency Power Cells' },
  exhaustmanifold:           { fr: "Collecteur d'échappement",           en: 'Exhaust Manifold' },
  shieldemitters:            { fr: 'Composants de protecteurs',          en: 'Shield Emitters' },
  energygridassembly:        { fr: "Conduits de transfert d'énergie",    en: 'Energy Grid Assembly' },
  powerconverter:            { fr: "Convertisseur d'énergie",            en: 'Power Converter' },
  iondistributor:            { fr: "Distributeurs d'ions",               en: 'Ion Distributor' },
  radiationbaffle:           { fr: 'Écran antiradiation',                en: 'Radiation Baffle' },
  marinesupplies:            { fr: 'Équipement aquamarin',               en: 'Marine Supplies' },
  geologicalequipment:       { fr: 'Équipement géologique',              en: 'Geological Equipment' },
  mineralextractors:         { fr: 'Extracteurs de minerai',             en: 'Mineral Extractors' },
  powergenerators:           { fr: 'Générateurs',                        en: 'Power Generators' },
  microbialfurnaces:         { fr: 'Hauts fourneaux microbiens',         en: 'Microbial Furnaces' },
  thermalcoolingunits:       { fr: 'Interconnexion dissipateur therm.',  en: 'Thermal Cooling Units' },
  cropharvesters:            { fr: 'Moissonneuses',                      en: 'Crop Harvesters' },
  articulationmotors:        { fr: 'Moteurs à articulation',             en: 'Articulation Motors' },
  reinforcedmountingplate:   { fr: 'Plaque de montage renforcée',        en: 'Reinforced Mounting Plate' },
  atmosphericprocessors:     { fr: 'Processeurs atmosphériques',         en: 'Atmospheric Processors' },
  hndshockmount:             { fr: 'Protection antichocs HP',            en: 'HN Shock Mount' },
  powergridassembly:         { fr: "Système de réseau d'alimentation",   en: 'Power Grid Assembly' },
  modularterminals:          { fr: 'Terminaux modulaires',               en: 'Modular Terminals' },
  coolingunits:              { fr: 'Unités de refroidissement',          en: 'Cooling Units' },
  waterpurifiers:            { fr: "Purificateurs d'eau",                en: 'Water Purifiers' },
  liquidoxygen:              { fr: 'Oxygène liquide',                    en: 'Liquid Oxygen' },
  surfacestabilisers:        { fr: 'Stabilisateurs de surface',          en: 'Surface Stabilisers' },
  structuralregulators:      { fr: 'Régulateurs structurels',            en: 'Structural Regulators' },
  mutomimager:               { fr: "Dispositif d'imagerie muonique",     en: 'Muon Tomography Imager' },

  // ── Médicaments ───────────────────────────────────────────────────────────
  agriculturalmedicines:     { fr: 'Agri-médicaments',         en: 'Agricultural Medicines' },
  progenitorcells:           { fr: 'Cellules souches',          en: 'Progenitor Cells' },
  basicmedicines:            { fr: 'Médicaments simples',       en: 'Basic Medicines' },
  advancedmedicines:         { fr: 'Médicaments complexes',     en: 'Advanced Medicines' },
  performanceenhancers:      { fr: 'Produits dopants',          en: 'Performance Enhancers' },
  combatantimutagens:        { fr: 'Stabilisateurs de combat',  en: 'Combat Stabilisers' },
  combatstabilisers:         { fr: 'Stabilisateurs de combat',  en: 'Combat Stabilisers' },

  // ── Nourritures ───────────────────────────────────────────────────────────
  algae:                     { fr: 'Algues',                    en: 'Algae' },
  coffee:                    { fr: 'Café',                      en: 'Coffee' },
  foodcartridges:            { fr: 'Cartouches alimentaires',   en: 'Food Cartridges' },
  grain:                     { fr: 'Céréales',                  en: 'Grain' },
  fruitandvegetables:        { fr: 'Fruits et légumes',         en: 'Fruit and Vegetables' },
  fish:                      { fr: 'Poisson',                   en: 'Fish' },
  tea:                       { fr: 'Thé',                       en: 'Tea' },
  meat:                      { fr: 'Viande',                    en: 'Meat' },
  animalmeat:                { fr: 'Viande animale',             en: 'Animal Meat' },
  syntheticmeat:             { fr: 'Viande synthétique',        en: 'Synthetic Meat' },

  // ── Produits chimiques ────────────────────────────────────────────────────
  nerveagents:               { fr: 'Agents neurotoxiques',              en: 'Nerve Agents' },
  hydrogenfuel:              { fr: 'Carburant à base d\'hydrogène',     en: 'Hydrogen Fuel' },
  water:                     { fr: 'Eau',                               en: 'Water' },
  rockforthfertiliser:       { fr: 'Engrais Rockforth',                 en: 'Rockforth Fertiliser' },
  explosives:                { fr: 'Explosifs',                         en: 'Explosives' },
  mineraloil:                { fr: 'Huiles minérales',                  en: 'Mineral Oil' },
  hydrogenperoxide:          { fr: "Peroxyde d'hydrogène",              en: 'Hydrogen Peroxide' },
  pesticides:                { fr: 'Pesticides',                        en: 'Pesticides' },
  syntheticreagents:         { fr: 'Réactifs synthétiques',             en: 'Synthetic Reagents' },
  agronomictreatment:        { fr: 'Traitement agronomique',            en: 'Agronomic Treatment' },
  tritium:                   { fr: 'Tritium',                           en: 'Tritium' },

  // ── Produits de consommation ──────────────────────────────────────────────
  consumertechnology:        { fr: 'Électronique grand public',  en: 'Consumer Technology' },
  survivalequipment:         { fr: 'Équipement de survie',        en: 'Survival Equipment' },
  domesticappliances:        { fr: 'Équipement ménager',          en: 'Domestic Appliances' },
  evacuationshelter:         { fr: "Abri d'urgence",              en: 'Evacuation Shelter' },
  hazardousenvironmentsuits: { fr: 'Combinaisons de protection',  en: 'Hazardous Environment Suits' },
  hazardenvironmentsuits:    { fr: 'Combinaisons de protection',  en: 'Hazardous Environment Suits' },
  clothing:                  { fr: 'Vêtements',                   en: 'Clothing' },

  // ── Drogues légales ───────────────────────────────────────────────────────
  beer:                      { fr: 'Bière',                             en: 'Beer' },
  bootlegliquor:             { fr: 'Liqueur de contrebande',            en: 'Bootleg Liquor' },
  narcotics:                 { fr: 'Narcotiques',                       en: 'Narcotics' },
  liquor:                    { fr: 'Spiritueux',                        en: 'Liquor' },
  tobacco:                   { fr: 'Tabac',                             en: 'Tobacco' },
  onionheadgammastrain:      { fr: "Variété gamma de tête d'oignon",    en: 'Onion Head Gamma Strain' },
  wine:                      { fr: 'Vin',                               en: 'Wine' },

  // ── Technologie ───────────────────────────────────────────────────────────
  diagnosticssensor:         { fr: "Capteur diagnostic d'équipement",  en: 'Diagnostics Sensor' },
  complexcatalysts:          { fr: 'Catalyseurs complexes',            en: 'Complex Catalysts' },
  computercomponents:        { fr: "Composants d'ordinateur",          en: 'Computer Components' },
  medicaldiagnosticequipment:{ fr: 'Équipement de diagnostic médical', en: 'Medical Diagnostic Equipment' },
  bioreducinglichen:         { fr: 'Lichen bioréducteur',              en: 'Bio-Reducing Lichen' },
  microcontrollers:          { fr: 'Microcontrôleurs',                 en: 'Microcontrollers' },
  nanodestructors:           { fr: 'Nanodestructeurs',                 en: 'Nanodestructors' },
  robotics:                  { fr: 'Robots',                           en: 'Robotics' },
  resonatingseparators:      { fr: 'Séparateurs à résonance',          en: 'Resonating Separators' },
  landenrichmentsystems:     { fr: 'Sys. enrichissement sols',         en: 'Land Enrichment Systems' },
  animalmonitor:             { fr: 'Sys. surveillance animale',        en: 'Animal Monitor' },
  animalnmonitor:            { fr: 'Sys. surveillance animale',        en: 'Animal Monitor' },
  telemetrysuite:            { fr: 'Système de télémétrie',            en: 'Telemetry Suite' },
  aquaponicssystems:         { fr: 'Systèmes aquaponiques',            en: 'Aquaponics Systems' },

  // ── Textiles ──────────────────────────────────────────────────────────────
  leather:                   { fr: 'Cuir',                             en: 'Leather' },
  naturalfabrics:            { fr: 'Fibres textiles naturelles',       en: 'Natural Fabrics' },
  syntheticfabrics:          { fr: 'Tissus synthétiques',              en: 'Synthetic Fabrics' },
  conductivefabrics:         { fr: 'Tissus conducteurs',               en: 'Conductive Fabrics' },
  militarygradefabrics:      { fr: 'Tissus militaires',                en: 'Military Grade Fabrics' },

  // ── Déchets ───────────────────────────────────────────────────────────────
  biowaste:                  { fr: 'Biodéchets',       en: 'Biowaste' },
  chemicalwaste:             { fr: 'Déchets chimiques', en: 'Chemical Waste' },
  toxicwaste:                { fr: 'Déchets toxiques',  en: 'Toxic Waste' },
  scrap:                     { fr: 'Ferraille',          en: 'Scrap' },

  // ── Armes ─────────────────────────────────────────────────────────────────
  personalweapons:           { fr: 'Armes de poing',       en: 'Personal Weapons' },
  nonlethalweapons:          { fr: 'Armes incapacitantes', en: 'Non-Lethal Weapons' },
  battleweapons:             { fr: 'Armes militaires',     en: 'Battle Weapons' },
  landmines:                 { fr: 'Mines terrestres',     en: 'Land Mines' },

  // ── Esclaves ──────────────────────────────────────────────────────────────
  slaves:                    { fr: 'Esclaves',          en: 'Slaves' },
  imperialslaves:            { fr: 'Esclaves impériaux', en: 'Imperial Slaves' },
};

/**
 * Découpe un nom PascalCase/camelCase en mots séparés.
 * "BattleWeapons" → "Battle Weapons", "liquidOxygen" → "Liquid Oxygen".
 * Utilisé comme fallback lisible quand aucune traduction n'est connue.
 */
function splitPascalCase(name: string): string {
  return name
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .trim();
}

/**
 * Retourne le libellé localisé d'une ressource CAPI.
 * Fallback : si la clé canonique est inconnue, découpe le PascalCase en mots lisibles.
 */
export function getResourceDisplayLabel(name: string, lang: 'fr' | 'en' = 'fr'): string {
  const key = canonicalCommodityKey(name);
  return COMMODITY_LABELS[key]?.[lang] ?? splitPascalCase(name);
}

/**
 * Clé unique pour regrouper besoins / inventaire (soute, FC) malgré FR/EN ou casse différente.
 */
export function canonicalCommodityKey(name: string): string {
  const n = normalizeCommodityKey(name);
  const direct = CANONICAL_LOOKUP.get(n);
  if (direct != null) return direct;
  const nAlnum = n.replace(/[^a-z0-9]/g, '');
  if (nAlnum.length < 2) return n;
  for (const [alias, canon] of CANONICAL_LOOKUP) {
    const aAlnum = alias.replace(/[^a-z0-9]/g, '');
    if (aAlnum === nAlnum) return canon;
  }
  return n;
}

/** Déduplique les chantiers par `id` (évite double comptage global si l’API renvoie des doublons). */
export function dedupeChantierSitesById(sites: readonly ActiveChantierSite[]): ActiveChantierSite[] {
  const seen = new Set<string>();
  const out: ActiveChantierSite[] = [];
  for (const site of sites) {
    if (seen.has(site.id)) continue;
    seen.add(site.id);
    out.push(site);
  }
  return out;
}

/**
 * Somme des `remaining` par marchandise (clé canonique), tous chantiers actifs « mine » du commandant.
 * Un chantier = une entrée par `chantierId` (pas de fusion ni doublon).
 */
/**
 * Fusionne une réponse inventaire avec le cache : ne remplace pas soute / FC si une erreur CAPI partielle
 * indique que l’agrégat n’est pas fiable pour cette partie.
 */
export function mergeInventoryDtos(
  previous: ChantierLogisticsInventoryDto | null,
  incoming: ChantierLogisticsInventoryDto,
): ChantierLogisticsInventoryDto {
  const shipOk = !incoming.shipCargoError;
  const fcOk = !incoming.carrierCargoError;
  return {
    shipCargoByName: shipOk ? { ...incoming.shipCargoByName } : { ...(previous?.shipCargoByName ?? {}) },
    carrierCargoByName: fcOk ? { ...incoming.carrierCargoByName } : { ...(previous?.carrierCargoByName ?? {}) },
    fetchedAtUtc: incoming.fetchedAtUtc,
    shipCargoError: incoming.shipCargoError,
    carrierCargoError: incoming.carrierCargoError,
    shipRateLimited: incoming.shipRateLimited,
    carrierRateLimited: incoming.carrierRateLimited,
    rateLimited: incoming.rateLimited,
    retryAfterSeconds: incoming.retryAfterSeconds ?? null,
    fleetCarrierSkippedDueToProfileRateLimit: incoming.fleetCarrierSkippedDueToProfileRateLimit,
  };
}

/**
 * En cas de 429 avec cache fusionné, les quantités restent affichables (pas de « — » généralisé).
 */
export function computeInventoryTrust(
  connected: boolean,
  inventoryHttpError: boolean,
  inv: ChantierLogisticsInventoryDto | null,
): InventoryTrust {
  if (!connected || inventoryHttpError || !inv) {
    return { shipKnown: false, carrierKnown: false };
  }
  const shipErr = inv.shipCargoError;
  const fcErr = inv.carrierCargoError;
  const hasShip = Object.keys(inv.shipCargoByName ?? {}).length > 0;
  const hasFc = Object.keys(inv.carrierCargoByName ?? {}).length > 0;
  const shipRl =
    inv.shipRateLimited === true || (shipErr?.includes('429') === true || shipErr?.includes('rate limit') === true);
  const fcRl =
    inv.carrierRateLimited === true ||
    (fcErr?.includes('429') === true || fcErr?.includes('rate limit') === true);
  const shipKnown = !shipErr || (shipRl && hasShip);
  const carrierKnown = !fcErr || (fcRl && hasFc);
  return { shipKnown, carrierKnown };
}

/** Log temporaire : payload soute brut vs affichage (test après déplacement cargaison). */
export function logShipCargoPayloadDiagnostic(inv: ChantierLogisticsInventoryDto | null): void {
  if (!inv) {
    console.debug('[Logistics][ShipDebug] inventaire null — pas de payload soute');
    return;
  }
  const raw = inv.shipCargoByName ?? {};
  console.debug('[Logistics][ShipDebug] soute — payload brut (API fusionnée client)', {
    fetchedAtUtc: inv.fetchedAtUtc,
    shipCargoError: inv.shipCargoError,
    shipRateLimited: inv.shipRateLimited,
    rawKeyCount: Object.keys(raw).length,
    shipCargoByName: raw,
  });
}

/** Debug : une ligne par ressource avec noms bruts / clé / quantités affichées. */
export function logInventoryMappingDebug(
  siteName: string,
  constructionResources: ConstructionResourceSnapshot[] | undefined,
  inv: ChantierLogisticsInventoryDto | null,
  trust: InventoryTrust,
): void {
  if (!constructionResources?.length) return;
  for (const r of constructionResources) {
    if (r.remaining <= 0) continue;
    const key = canonicalCommodityKey(r.name);
    const shipQty = lookupCargoQty(inv?.shipCargoByName, r.name);
    const fcQty = lookupCargoQty(inv?.carrierCargoByName, r.name);
    const shipKeys =
      inv?.shipCargoByName != null
        ? Object.keys(inv.shipCargoByName).filter((k) => canonicalCommodityKey(k) === key)
        : [];
    const fcKeys =
      inv?.carrierCargoByName != null
        ? Object.keys(inv.carrierCargoByName).filter((k) => canonicalCommodityKey(k) === key)
        : [];
    console.debug('[Logistics] inventory mapping', {
      siteName,
      commodityChantier: r.name,
      canonicalKey: key,
      commodityRawShipKeys: shipKeys,
      commodityRawFcKeys: fcKeys,
      qtyShipFound: shipQty,
      qtyFcFound: fcQty,
      qtyShipDisplayed: trust.shipKnown ? shipQty : '—',
      qtyFcDisplayed: trust.carrierKnown ? fcQty : '—',
    });
  }
}

/** Liste des besoins par chantier (debug global). */
export function logGlobalRequirementsRawByChantier(mineSites: readonly ActiveChantierSite[]): void {
  const uniqueSites = dedupeChantierSitesById(mineSites);
  console.debug('[Logistics] global requirements raw (by chantier):');
  for (const site of uniqueSites) {
    if (!site.active) continue;
    console.debug(`  chantierId=${site.id} siteName=${site.stationName ?? '—'}`, {
      requirements: site.constructionResources?.map((r) => ({ name: r.name, remaining: r.remaining })),
    });
  }
}

export function buildGlobalNeedByCommodityMap(mineSites: readonly ActiveChantierSite[]): Map<string, number> {
  const map = new Map<string, number>();
  const uniqueSites = dedupeChantierSitesById(mineSites);
  for (const site of uniqueSites) {
    if (!site.active) continue;
    for (const r of site.constructionResources ?? []) {
      if (r.remaining <= 0) continue;
      const key = canonicalCommodityKey(r.name);
      map.set(key, (map.get(key) ?? 0) + r.remaining);
    }
  }
  return map;
}

function globalNeedForCommodity(globalMap: Map<string, number>, commodityName: string): number {
  const key = canonicalCommodityKey(commodityName);
  return globalMap.get(key) ?? 0;
}

/**
 * Quantité dans une map nom → qty : somme toutes les clés qui se résolvent vers la même commodité canonique.
 */
export function lookupCargoQty(map: Record<string, number> | undefined, commodityName: string): number {
  if (!map || !commodityName?.trim()) return 0;
  const target = canonicalCommodityKey(commodityName);
  let sum = 0;
  for (const [k, v] of Object.entries(map)) {
    if (canonicalCommodityKey(k) === target) sum += Math.max(0, v);
  }
  return sum;
}

function computeStatus(
  need: number,
  shipQty: number,
  carrierQty: number,
  trust: InventoryTrust,
): ResourceAvailabilityStatus {
  let sum = 0;
  if (trust.shipKnown) sum += shipQty;
  if (trust.carrierKnown) sum += carrierQty;
  if (!trust.shipKnown && !trust.carrierKnown) return 'zero';
  if (sum === 0) return 'zero';
  if (sum >= need) return 'ok';
  return 'warn';
}

/**
 * Lignes ressources : besoin chantier vs soute vaisseau / FC (stocks séparés).
 * `constructionResources` doit être exclusivement ceux du chantier courant (pas de fusion).
 */
export function buildChantierResourceRows(
  constructionResources: ConstructionResourceSnapshot[] | undefined,
  shipCargoByName: Record<string, number>,
  carrierCargoByName: Record<string, number>,
  trust: InventoryTrust,
  globalNeedByCommodity: Map<string, number>,
): ChantierResourceRowVm[] {
  if (!constructionResources?.length) return [];
  const rows: ChantierResourceRowVm[] = [];
  for (const r of constructionResources) {
    if (r.remaining <= 0) continue;
    const shipQty = lookupCargoQty(shipCargoByName, r.name);
    const carrierQty = lookupCargoQty(carrierCargoByName, r.name);
    const status = computeStatus(r.remaining, shipQty, carrierQty, trust);
    const globalNeed = globalNeedForCommodity(globalNeedByCommodity, r.name);
    rows.push({
      name: r.name,
      displayName: getResourceDisplayLabel(r.name, 'fr'),
      need: r.remaining,
      globalNeed,
      shipQty,
      carrierQty,
      status,
    });
  }
  rows.sort((a, b) => b.need - a.need);
  return rows;
}

/** Somme affichable pour la colonne Total (uniquement les stocks connus). */
export function knownStockSum(row: ChantierResourceRowVm, trust: InventoryTrust): number {
  let s = 0;
  if (trust.shipKnown) s += row.shipQty;
  if (trust.carrierKnown) s += row.carrierQty;
  return s;
}

/** Afficher une colonne Total secondaire (au moins un stock connu). */
export function showTotalColumn(trust: InventoryTrust): boolean {
  return trust.shipKnown || trust.carrierKnown;
}

export interface StationDisplayParts {
  /** Libellé type (ex. « Planetary Construction Site: ») — affiché en atténué. */
  prefix: string | null;
  /** Nom du site (plein contraste). */
  name: string;
}

/**
 * Sépare « Planetary Construction Site: Brouwer » / « Orbital Construction Site: … » en préfixe + nom.
 * Tout texte sans ce motif reste entièrement dans `name`.
 */
export function splitStationDisplayLabel(stationName: string | null | undefined): StationDisplayParts {
  const s = (stationName ?? '').trim();
  if (!s) return { prefix: null, name: '—' };
  const sep = ': ';
  const i = s.indexOf(sep);
  if (i === -1) return { prefix: null, name: s };
  const left = s.slice(0, i).trim();
  const right = s.slice(i + sep.length).trim();
  if (!right) return { prefix: null, name: s };
  if (/\bconstruction\s+site$/i.test(left)) {
    return { prefix: `${left}: `, name: right };
  }
  return { prefix: null, name: s };
}
