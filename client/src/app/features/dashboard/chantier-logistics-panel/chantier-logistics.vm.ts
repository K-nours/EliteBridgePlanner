import type { ChantierLogisticsInventoryDto } from '../../../core/models/chantier-logistics-inventory.model';
import type {
  ActiveChantierSite,
  ConstructionResourceSnapshot,
} from '../../../core/state/active-chantiers.store';

export type ResourceAvailabilityStatus = 'ok' | 'warn' | 'zero';

/** Indique si les quantités côté CAPI sont exploitables (sinon afficher « — »). */
export interface InventoryTrust {
  carrierKnown: boolean;
}

export interface ChantierResourceRowVm {
  name: string;
  /** Libellé localisé (FR ou EN) — fallback sur `name` brut si inconnu. */
  displayName: string;
  need: number;
  /** Somme des besoins restants pour cette marchandise, tous chantiers actifs « mine » du commandant (clé unique chantier). */
  globalNeed: number;
  carrierQty: number;
  status: ResourceAvailabilityStatus;
  /** Reste à prendre pour couvrir CE chantier : max(0, need - FC). */
  resteChantier: number;
  /** Reste à prendre pour couvrir TOUS les chantiers : max(0, globalNeed - FC). */
  resteGlobal: number;
}

function normalizeCommodityKey(s: string): string {
  return s.trim().toLowerCase().replace(/\s+/g, ' ');
}

/**
 * Groupes de synonymes (FR/EN / orthographe CAPI) → même commodité.
 * La clé canonique est le 1er terme du groupe (normalisé lowercase).
 * Doit rester synchronisé avec NormalizeCommodityKey (FrontierLogisticsInventoryService.cs).
 */
const COMMODITY_EQUIVALENCE_GROUPS: readonly (readonly string[])[] = [
  // ── Métaux ────────────────────────────────────────────────────────────────
  ['steel', 'acier'],
  ['aluminium', 'aluminum'],
  ['silver', 'argent'],
  ['beryllium', 'béryllium'],
  ['bismuth'],
  ['cobalt'],
  ['copper', 'cuivre'],
  ['gallium'],
  ['hafnium178', 'hafnium 178'],
  ['indium'],
  ['lanthanum', 'lanthane'],
  ['lithium'],
  ['gold', 'or'],
  ['osmium'],
  ['palladium'],
  ['platinum', 'platine'],
  ['praseodymium', 'praséodyme', 'praseodyme'],
  ['samarium'],
  ['tantalum', 'tantale'],
  ['thallium'],
  ['thorium'],
  ['titanium', 'titane'],
  ['uranium'],
  ['lead', 'plomb'],
  ['zinc'],
  ['nickel'],
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

  // ── Minéraux ──────────────────────────────────────────────────────────────
  ['alexandrite'],
  ['bauxite'],
  ['benitoite', 'bénitoïte'],
  ['bertrandite'],
  ['bromellite'],
  ['siliconcarbidefibres', 'silicon carbide fibres', 'carbure de silicium'],
  ['coltan'],
  ['methanolmonohydratecrystals', 'methanol monohydrate crystals',
   'cristaux de méthanol monohydraté', 'cristaux de methanol monohydrate'],
  ['cryolite'],
  ['lowtemperaturediamond', 'low temperature diamonds', 'low temperature diamond',
   'diamants basse température', 'diamants basse temperature'],
  ['gallite'],
  ['goslarite'],
  ['grandidierite', 'grandidiérite'],
  ['hematite', 'hématite'],
  ['methanehydrate', 'methane hydrate', 'hydrate de méthane', 'hydrate de methane'],
  ['lithiumhydroxide', 'lithium hydroxide', 'hydroxyde de lithium'],
  ['indite'],
  ['jadeite', 'jadéite'],
  ['lepidolite', 'lépidolite'],
  ['monazite'],
  ['musgravite'],
  ['voidopal', 'void opal', 'opale du vide'],
  ['painite'],
  ['pyrophyllite'],
  ['rhodplumsite'],
  ['rutile'],
  ['serendibite'],
  ['taaffeite', 'taafféite'],
  ['uraninite'],

  // ── Matériaux industriels ─────────────────────────────────────────────────
  ['ceramiccomposites', 'ceramic composites', 'CeramicComposites', 'Ceramic Composites',
   'composites céramiques', 'composites ceramiques',
   'composés en céramique', 'composes en ceramique', 'composés céramiques'],
  ['polymers', 'Polymers', 'polymères', 'polymeres', 'polymère(s)', 'polymere(s)'],
  ['semiconductors', 'Semiconductors', 'semi-conducteurs', 'semiconducteurs', 'semi-conducteur(s)'],
  ['superconductors', 'Superconductors', 'supraconducteurs', 'supraconducteur(s)'],
  ['cmmcomposite', 'CMMComposite', 'cmm composite', 'CMM Composite', 'composite mmc'],
  ['insulatingmembrane', 'InsulatingMembrane', 'Insulating Membrane', 'membrane isolante'],
  ['neofabricinsulation', 'Neofabric Insulation', 'isolant en néotextile', 'isolant en neotextile'],
  ['metaalloys', 'Meta-Alloys', 'méta-alliages', 'meta-alliages'],
  ['coolinghoses', 'Cooling Hoses', 'tuyaux de refroidissement'],
  ['reactivearmour', 'ReactiveArmour', 'Reactive Armour', 'Reactive Armor', 'armure réactive', 'armure reactive'],
  ['reactivearmouring', 'Reactive Armouring', 'protection réactive', 'protection reactive'],

  // ── Machines ──────────────────────────────────────────────────────────────
  ['autofabricators', 'Auto-Fabricators', 'auto-bâtisseurs', 'auto-batisseurs'],
  ['buildingfabricators', 'BuildingFabricators', 'Building Fabricators',
   'fabricants de bâtiments', 'fabricants de batiments'],
  ['magneticemittercoil', 'Magnetic Emitter Coil',
   "bobine d'émission magnétique", 'bobine d emission magnetique'],
  ['emergencypowercells', 'Emergency Power Cells',
   "cellules d'énergie de secours", 'cellules d energie de secours'],
  ['exhaustmanifold', 'Exhaust Manifold',
   "collecteur d'échappement", 'collecteur d echappement'],
  ['shieldemitters', 'Shield Emitters', 'composants de protecteurs'],
  ['energygridassembly', 'Energy Grid Assembly',
   "conduits de transfert d'énergie", 'conduits de transfert d energie'],
  ['powerconverter', 'Power Converter',
   "convertisseur d'énergie", 'convertisseur d energie'],
  ['iondistributor', 'Ion Distributor',
   "distributeurs d'ions", 'distributeurs d ions'],
  ['radiationbaffle', 'Radiation Baffle', 'écran antiradiation', 'ecran antiradiation'],
  ['marinesupplies', 'Marine Supplies', 'équipement aquamarin', 'equipement aquamarin'],
  ['geologicalequipment', 'Geological Equipment',
   'équipement géologique', 'equipement geologique'],
  ['mineralextractors', 'Mineral Extractors', 'extracteurs de minerai'],
  ['powergenerators', 'PowerGenerators', 'Power Generators', 'générateurs', 'generateurs'],
  ['microbialfurnaces', 'Microbial Furnaces', 'hauts fourneaux microbiens'],
  ['thermalcoolingunits', 'Thermal Cooling Units',
   'interconnexion dissipateur therm.', 'interconnexion dissipateur thermique'],
  ['cropharvesters', 'Crop Harvesters', 'moissonneuses'],
  ['articulationmotors', 'Articulation Motors',
   'moteurs à articulation', 'moteurs a articulation'],
  ['reinforcedmountingplate', 'Reinforced Mounting Plate',
   'plaque de montage renforcée', 'plaque de montage renforcee'],
  ['atmosphericprocessors', 'Atmospheric Processors',
   'processeurs atmosphériques', 'processeurs atmospheriques'],
  ['hndshockmount', 'HN Shock Mount', 'protection antichocs hp'],
  ['powergridassembly', 'Power Grid Assembly',
   "système de réseau d'alimentation", 'systeme de reseau d alimentation'],
  ['modularterminals', 'Modular Terminals', 'terminaux modulaires'],
  ['coolingunits', 'Cooling Units', 'unités de refroidissement', 'unites de refroidissement'],
  ['waterpurifiers', 'WaterPurifiers', 'Water Purifiers',
   "purificateurs d'eau", 'purificateurs d eau'],
  ['liquidoxygen', 'LiquidOxygen', 'Liquid Oxygen', 'oxygène liquide', 'oxygene liquide'],
  ['surfacestabilisers', 'Surface Stabilisers', 'stabilisateurs de surface'],
  ['structuralregulators', 'Structural Regulators',
   'régulateurs structurels', 'regulateurs structurels'],
  ['mutomimager', 'Muon Tomography Imager',
   "dispositif d'imagerie muonique", 'dispositif d imagerie muonique'],
  ['computercomponents', 'ComputerComponents', 'Computer Components',
   "composants d'ordinateur", 'composants d ordinateur'],
  ['landenrichmentsystems', 'Land Enrichment Systems', 'landenviromentalsystems',
   "systèmes d'enrichissement", 'systemes d enrichissement', 'sys. enrichissement sols'],
  ['animalmonitor', 'Animal Monitor', 'animalnmonitor',
   'sys. surveillance animale', 'système de surveillance animale'],
  ['telemetrysuite', 'Telemetry Suite', 'système de télémétrie', 'systeme de telemetrie'],
  ['aquaponicssystems', 'Aquaponics Systems',
   'systèmes aquaponiques', 'systemes aquaponiques'],
  ['bioreducinglichen', 'Bio-Reducing Lichen',
   'lichen bioréducteur', 'lichen bioreducteur'],
  ['microcontrollers', 'microcontrôleurs', 'microcontroleurs'],
  ['nanodestructors', 'nanodestructeurs'],
  ['robotics', 'robots'],
  ['resonatingseparators', 'Resonating Separators',
   'séparateurs à résonance', 'separateurs a resonance'],
  ['complexcatalysts', 'Complex Catalysts', 'advanced catalysers', 'catalyseurs complexes'],
  ['diagnosticssensor', 'Diagnostics Sensor',
   "capteur diagnostic d'équipement", 'capteur diagnostic d equipement'],
  ['medicaldiagnosticequipment', 'Medical Diagnostic Equipment',
   'équipement de diagnostic médical', 'equipement de diagnostic medical'],

  // ── Médicaments ───────────────────────────────────────────────────────────
  ['agriculturalmedicines', 'Agricultural Medicines',
   'agri-médicaments', 'agri-medicaments'],
  ['progenitorcells', 'Progenitor Cells', 'cellules souches'],
  ['basicmedicines', 'Basic Medicines', 'médicaments simples', 'medicaments simples'],
  ['advancedmedicines', 'Advanced Medicines', 'médicaments complexes', 'medicaments complexes'],
  ['performanceenhancers', 'Performance Enhancers', 'produits dopants'],
  ['combatantimutagens', 'Combat Antimutagens', 'stabilisateurs de combat'],
  ['combatstabilisers', 'Combat Stabilisers', 'Combat Stabilizers'],

  // ── Nourriture ────────────────────────────────────────────────────────────
  ['algae', 'algues'],
  ['coffee', 'café', 'cafe'],
  ['foodcartridges', 'FoodCartridges', 'Food Cartridges', 'cartouches alimentaires',
   'cartouche(s) alimentaire(s)', 'cartouche(s) alimentaires'],
  ['grain', 'céréales', 'cereales'],
  ['fruitandvegetables', 'FruitAndVegetables', 'Fruit and Vegetables',
   'fruits et légumes', 'fruits et legumes'],
  ['fish', 'poisson'],
  ['tea', 'thé', 'the'],
  ['meat', 'viande'],
  ['animalmeat', 'Animal Meat', 'viande animale'],
  ['syntheticmeat', 'Synthetic Meat', 'viande synthétique', 'viande synthetique'],

  // ── Produits chimiques ────────────────────────────────────────────────────
  ['nerveagents', 'Nerve Agents', 'agents neurotoxiques'],
  ['hydrogenfuel', 'Hydrogen Fuel',
   "carburant à base d'hydrogène", 'carburant a base d hydrogene'],
  ['water', 'eau'],
  ['rockforthfertiliser', 'Rockforth Fertiliser', 'Rockforth Fertilizer', 'engrais rockforth'],
  ['explosives', 'explosifs'],
  ['mineraloil', 'Mineral Oil', 'huiles minérales', 'huiles minerales'],
  ['hydrogenperoxide', 'Hydrogen Peroxide',
   "peroxyde d'hydrogène", 'peroxyde d hydrogene'],
  ['pesticides'],
  ['syntheticreagents', 'Synthetic Reagents',
   'réactifs synthétiques', 'reactifs synthetiques'],
  ['agronomictreatment', 'Agronomic Treatment', 'traitement agronomique'],
  ['tritium'],

  // ── Produits de consommation ──────────────────────────────────────────────
  ['consumertechnology', 'Consumer Technology',
   'électronique grand public', 'electronique grand public'],
  ['survivalequipment', 'Survival Equipment',
   'équipement de survie', 'equipement de survie'],
  ['domesticappliances', 'Domestic Appliances',
   'équipement ménager', 'equipement menager'],
  ['evacuationshelter', 'Evacuation Shelter',
   "abri d'urgence", 'abri d urgence'],
  ['hazardousenvironmentsuits', 'Hazardous Environment Suits',
   'Hazard Environment Suits', 'combinaisons de protection'],
  ['clothing', 'vêtements', 'vetements'],

  // ── Drogues légales ───────────────────────────────────────────────────────
  ['beer', 'bière', 'biere'],
  ['bootlegliquor', 'Bootleg Liquor', 'liqueur de contrebande'],
  ['narcotics', 'narcotiques'],
  ['liquor', 'spiritueux'],
  ['tobacco', 'tabac'],
  ['onionheadgammastrain', 'Onion Head Gamma Strain',
   "variété gamma de tête d'oignon", 'variete gamma de tete d oignon'],
  ['wine', 'vin'],

  // ── Textiles ──────────────────────────────────────────────────────────────
  ['leather', 'cuir'],
  ['naturalfabrics', 'Natural Fabrics', 'fibres textiles naturelles'],
  ['syntheticfabrics', 'Synthetic Fabrics',
   'tissus synthétiques', 'tissus synthetiques'],
  ['conductivefabrics', 'Conductive Fabrics', 'tissus conducteurs'],
  ['militarygradefabrics', 'Military Grade Fabrics', 'tissus militaires'],

  // ── Déchets ───────────────────────────────────────────────────────────────
  ['biowaste', 'biodéchets', 'biodechets'],
  ['chemicalwaste', 'Chemical Waste', 'déchets chimiques', 'dechets chimiques'],
  ['toxicwaste', 'Toxic Waste', 'déchets toxiques', 'dechets toxiques'],
  ['scrap', 'ferraille'],

  // ── Armes ─────────────────────────────────────────────────────────────────
  ['personalweapons', 'Personal Weapons', 'armes de poing'],
  ['nonlethalweapons', 'Non-Lethal Weapons', 'Non Lethal Weapons', 'armes incapacitantes'],
  ['battleweapons', 'Battle Weapons', 'armes militaires'],
  ['landmines', 'Land Mines', 'mines terrestres'],

  // ── Esclaves ──────────────────────────────────────────────────────────────
  ['slaves', 'esclaves'],
  ['imperialslaves', 'Imperial Slaves', 'esclaves impériaux', 'esclaves imperiaux'],
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
  thermalcoolingunits:       { fr: 'Unités de refroidissement',         en: 'Thermal Cooling Units' },
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
 * Fusionne une réponse inventaire avec le cache : ne remplace pas le FC si une erreur CAPI partielle
 * indique que l’agrégat n’est pas fiable.
 */
export function mergeInventoryDtos(
  previous: ChantierLogisticsInventoryDto | null,
  incoming: ChantierLogisticsInventoryDto,
): ChantierLogisticsInventoryDto {
  const fcOk = !incoming.carrierCargoError;
  return {
    carrierCargoByName: fcOk ? { ...incoming.carrierCargoByName } : { ...(previous?.carrierCargoByName ?? {}) },
    fetchedAtUtc: incoming.fetchedAtUtc,
    carrierCargoError: incoming.carrierCargoError,
    carrierRateLimited: incoming.carrierRateLimited,
    rateLimited: incoming.rateLimited,
    retryAfterSeconds: incoming.retryAfterSeconds ?? null,
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
    return { carrierKnown: false };
  }
  const fcErr = inv.carrierCargoError;
  const hasFc = Object.keys(inv.carrierCargoByName ?? {}).length > 0;
  const fcRl =
    inv.carrierRateLimited === true ||
    (fcErr?.includes('429') === true || fcErr?.includes('rate limit') === true);
  const carrierKnown = !fcErr || (fcRl && hasFc);
  return { carrierKnown };
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
    const fcQty = lookupCargoQty(inv?.carrierCargoByName, r.name);
    const fcKeys =
      inv?.carrierCargoByName != null
        ? Object.keys(inv.carrierCargoByName).filter((k) => canonicalCommodityKey(k) === key)
        : [];
    console.debug('[Logistics] inventory mapping', {
      siteName,
      commodityChantier: r.name,
      canonicalKey: key,
      commodityRawFcKeys: fcKeys,
      qtyFcFound: fcQty,
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
  carrierQty: number,
  trust: InventoryTrust,
  globalNeed: number,
): ResourceAvailabilityStatus {
  if (!trust.carrierKnown) return 'zero';
  const sum = carrierQty;
  if (sum === 0) return 'zero';
  // Vert seulement si le stock couvre TOUS les chantiers (globalNeed).
  // Orange si ça couvre ce chantier mais pas le global.
  if (sum >= Math.max(need, globalNeed)) return 'ok';
  return 'warn';
}

/**
 * Lignes ressources : besoin chantier vs FC (soute vaisseau non disponible via CAPI).
 * `constructionResources` doit être exclusivement ceux du chantier courant (pas de fusion).
 */
export function buildChantierResourceRows(
  constructionResources: ConstructionResourceSnapshot[] | undefined,
  carrierCargoByName: Record<string, number>,
  trust: InventoryTrust,
  globalNeedByCommodity: Map<string, number>,
): ChantierResourceRowVm[] {
  if (!constructionResources?.length) return [];
  const rows: ChantierResourceRowVm[] = [];
  for (const r of constructionResources) {
    if (r.remaining <= 0) continue;
    const carrierQty = lookupCargoQty(carrierCargoByName, r.name);
    const globalNeed = globalNeedForCommodity(globalNeedByCommodity, r.name);
    const status = computeStatus(r.remaining, carrierQty, trust, globalNeed);
    const knownSum = trust.carrierKnown ? carrierQty : 0;
    const resteChantier = Math.max(0, r.remaining - knownSum);
    const resteGlobal = Math.max(0, globalNeed - knownSum);
    rows.push({
      name: r.name,
      displayName: getResourceDisplayLabel(r.name, 'fr'),
      need: r.remaining,
      globalNeed,
      carrierQty,
      status,
      resteChantier,
      resteGlobal,
    });
  }
  rows.sort((a, b) => b.need - a.need);
  return rows;
}

/** Somme affichable pour la colonne Total (FC uniquement). */
export function knownStockSum(row: ChantierResourceRowVm, trust: InventoryTrust): number {
  return trust.carrierKnown ? row.carrierQty : 0;
}

/** Afficher une colonne Total secondaire (FC connu). */
export function showTotalColumn(trust: InventoryTrust): boolean {
  return trust.carrierKnown;
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
