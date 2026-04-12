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
