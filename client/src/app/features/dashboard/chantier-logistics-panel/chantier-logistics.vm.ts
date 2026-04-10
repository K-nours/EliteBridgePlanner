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
  /** Somme des besoins restants pour cette marchandise, tous chantiers du commandant (mine). */
  globalNeed: number;
  shipQty: number;
  carrierQty: number;
  status: ResourceAvailabilityStatus;
}

function normalizeCommodityKey(s: string): string {
  return s.trim().toLowerCase().replace(/\s+/g, ' ');
}

/**
 * Somme des `remaining` par marchandise (clé normalisée), tous chantiers actifs « mine » du commandant.
 */
export function buildGlobalNeedByCommodityMap(mineSites: readonly ActiveChantierSite[]): Map<string, number> {
  const map = new Map<string, number>();
  for (const site of mineSites) {
    if (!site.active) continue;
    for (const r of site.constructionResources ?? []) {
      if (r.remaining <= 0) continue;
      const key = normalizeCommodityKey(r.name);
      map.set(key, (map.get(key) ?? 0) + r.remaining);
    }
  }
  return map;
}

function globalNeedForCommodity(globalMap: Map<string, number>, commodityName: string): number {
  const key = normalizeCommodityKey(commodityName);
  let v = globalMap.get(key);
  if (v != null) return v;
  const keyAlnum = key.replace(/[^a-z0-9]/g, '');
  if (keyAlnum.length < 2) return 0;
  for (const [k, sum] of globalMap) {
    const kAlnum = k.replace(/[^a-z0-9]/g, '');
    if (kAlnum === keyAlnum) return sum;
  }
  return 0;
}

/**
 * Quantité dans une map nom → qty, avec matching insensible à la casse / espaces,
 * puis repli sur comparaison alphanumérique (réduit les faux 0).
 */
export function lookupCargoQty(map: Record<string, number> | undefined, commodityName: string): number {
  if (!map || !commodityName?.trim()) return 0;
  const n = normalizeCommodityKey(commodityName);
  for (const [k, v] of Object.entries(map)) {
    if (normalizeCommodityKey(k) === n) return Math.max(0, v);
  }
  const nAlnum = n.replace(/[^a-z0-9]/g, '');
  if (nAlnum.length < 2) return 0;
  for (const [k, v] of Object.entries(map)) {
    const kAlnum = normalizeCommodityKey(k).replace(/[^a-z0-9]/g, '');
    if (kAlnum === nAlnum) return Math.max(0, v);
  }
  return 0;
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
