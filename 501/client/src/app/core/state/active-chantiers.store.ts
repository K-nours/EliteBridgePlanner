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
 * L’id est toujours l’identifiant SQL renvoyé par l’API (pas de génération locale).
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

  /**
   * Chantiers uniques par `id` : d’abord « mine » (ordre conservé), puis « others » sans doublon d’id.
   * Évite deux lignes avec le même chantier (double comptage global / mauvaise résolution).
   */
  readonly entries = computed(() => {
    const mine = this._mine();
    const others = this._others();
    const seen = new Set<string>();
    const out: ActiveChantierSite[] = [];
    for (const s of mine) {
      if (seen.has(s.id)) continue;
      seen.add(s.id);
      out.push(s);
    }
    for (const s of others) {
      if (seen.has(s.id)) continue;
      seen.add(s.id);
      out.push(s);
    }
    return out;
  });

  /** Remplace entièrement les listes (source de vérité serveur après GET / persist / delete). */
  replaceMineAndOthers(mine: ActiveChantierSite[], others: ActiveChantierSite[]): void {
    this._mine.set([...mine]);
    this._others.set([...others]);
  }
}
