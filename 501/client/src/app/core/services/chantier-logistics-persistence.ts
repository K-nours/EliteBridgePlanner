/**
 * Persistance locale du Real sync et des horodatages par chantier (SQL id),
 * pour survivre au F5 et cohérence UI / polling.
 */
const STORAGE_KEY = '501.chantierLogistics.realSync.v1';

export interface ChantierLogisticsPersistedSlice {
  enabled: boolean;
  lastChantierDataRefreshSuccessAt: string | null;
  lastRealSyncSuccessAt: string | null;
  lastRealSyncAttemptAt: string | null;
  lastRealSyncAttemptOutcome: 'success' | 'failure' | null;
  /** Dernière fois où les besoins chantier ont été vérifiés avec succès via CAPI (refresh-one OK). */
  lastChantierNeedsVerifiedAt: string | null;
  /** Code métier si marketId illisible côté CAPI (ex. MARKET_NO_CHANTIER_CAPI_BLOCK). */
  chantierMarketIdIssueCode: string | null;
  chantierMarketIdIssueMessage: string | null;
  chantierMarketIdIssueRequiresRedock?: boolean | null;
}

export type ChantierLogisticsPersistedMap = Record<string, ChantierLogisticsPersistedSlice>;

function safeParse(raw: string | null): ChantierLogisticsPersistedMap {
  if (raw == null || raw === '') return {};
  try {
    const o = JSON.parse(raw) as unknown;
    if (o == null || typeof o !== 'object' || Array.isArray(o)) return {};
    return o as ChantierLogisticsPersistedMap;
  } catch {
    return {};
  }
}

export function loadChantierLogisticsPersistence(): ChantierLogisticsPersistedMap {
  if (typeof localStorage === 'undefined') return {};
  return safeParse(localStorage.getItem(STORAGE_KEY));
}

export function saveChantierLogisticsPersistence(map: ChantierLogisticsPersistedMap): void {
  if (typeof localStorage === 'undefined') return;
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(map));
  } catch {
    /* quota / mode privé */
  }
}

export function readSliceForChantier(chantierId: string): ChantierLogisticsPersistedSlice | null {
  const map = loadChantierLogisticsPersistence();
  const s = map[chantierId];
  return s ?? null;
}

export function writeSliceForChantier(chantierId: string, slice: ChantierLogisticsPersistedSlice): void {
  const map = loadChantierLogisticsPersistence();
  map[chantierId] = slice;
  saveChantierLogisticsPersistence(map);
}
