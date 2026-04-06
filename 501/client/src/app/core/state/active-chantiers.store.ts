import { Injectable, computed, signal } from '@angular/core';

/** Snapshot léger renvoyé par le serveur (liste déjà bornée). */
export interface ConstructionResourceSnapshot {
  name: string;
  required: number;
  provided: number;
  remaining: number;
}

/**
 * Chantier déclaré persisté côté serveur (guilde).
 */
export interface ActiveChantierSite {
  readonly id: string;
  systemName: string;
  stationName: string;
  cmdrName: string | null;
  /** Toujours true pour les entrées listées ; prévu pour archivage ultérieur. */
  active: boolean;
  declaredAt: string;
  marketId?: string | null;
  /** Ressources de construction (échantillon serveur + total réel). */
  constructionResources?: ConstructionResourceSnapshot[];
  constructionResourcesTotal?: number;
}

@Injectable({ providedIn: 'root' })
export class ActiveChantiersStore {
  private readonly _mine = signal<ActiveChantierSite[]>([]);
  private readonly _others = signal<ActiveChantierSite[]>([]);

  /** Mes chantiers (GET chantiers-declared/me). */
  readonly mine = this._mine.asReadonly();
  /** Autres CMDRs (GET chantiers-declared/others). */
  readonly others = this._others.asReadonly();

  /** Vue combinée pour sélection logistique / résolution par id. */
  readonly entries = computed(() => [...this._mine(), ...this._others()]);

  /** Source de vérité après chargement ou refresh double flux me + others. */
  replaceMineAndOthers(mine: ActiveChantierSite[], others: ActiveChantierSite[]): void {
    this._mine.set([...mine]);
    this._others.set([...others]);
  }

  /**
   * Ajoute un chantier si absent : déduplication par marketId si fourni, sinon par (système, station).
   */
  tryAdd(entry: {
    systemName: string;
    stationName: string;
    cmdrName: string | null;
    marketId?: string | null;
    constructionResources?: ConstructionResourceSnapshot[];
    constructionResourcesTotal?: number;
    declaredAt?: string;
  }): { added: true } | { added: false; duplicate: true } {
    const system = entry.systemName.trim();
    const station = entry.stationName.trim();
    const mid = entry.marketId?.trim() ?? '';
    const dup = this.entries().some(
      (e) => e.active && isDuplicate(e, system, station, mid || undefined),
    );
    if (dup) return { added: false, duplicate: true };

    const row: ActiveChantierSite = {
      id: crypto.randomUUID(),
      systemName: system,
      stationName: station,
      cmdrName: entry.cmdrName,
      active: true,
      declaredAt: entry.declaredAt ?? new Date().toISOString(),
      marketId: mid || undefined,
      constructionResources: entry.constructionResources,
      constructionResourcesTotal: entry.constructionResourcesTotal,
    };
    this._mine.update((list) => [...list, row]);
    return { added: true };
  }
}

function isDuplicate(
  existing: ActiveChantierSite,
  systemName: string,
  stationName: string,
  marketId?: string,
): boolean {
  if (marketId && existing.marketId && existing.marketId === marketId) return true;
  return siteKey(existing.systemName, existing.stationName) === siteKey(systemName, stationName);
}

function siteKey(systemName: string, stationName: string): string {
  return `${systemName.trim().toLowerCase()}\n${stationName.trim().toLowerCase()}`;
}
