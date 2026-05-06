import { Injectable, inject } from '@angular/core';
import { Observable, finalize, of, shareReplay } from 'rxjs';
import { ChantierLogisticsInventoryApiService } from './chantier-logistics-inventory-api.service';
import type { ChantierLogisticsInventoryDto } from '../models/chantier-logistics-inventory.model';

/**
 * Compteurs **un cycle Real sync** (entre `beginRealSyncCycle` et `logRealSyncCycleVolume`).
 * Mis à jour **uniquement** pour `getInventory$(reason === 'real-sync')`.
 *
 * **Pas de champ `skipped` ici** : l'anti-spam « effet chantier » (intervalle min) est compté en session
 * dans {@link ChantierLogisticsInventoryCoordinatorService.volume}`.skippedEffectMinInterval` — ce chemin est
 * inactif quand Real sync est ON (l'effet n'émet pas de GET inventaire).
 */
export interface RealSyncInventoryCycleSnapshot {
  /** GET inventaire réellement démarrés ce cycle (flux `real-sync`). */
  getIssued: number;
  /** Requêtes `real-sync` fusionnées avec un GET déjà en vol ce cycle. */
  dedupedInflight: number;
}

/**
 * Mutualise les GET inventaire logistique (même endpoint pour toute la session) :
 * déduplication des requêtes en cours, intervalle minimum côté effet « changement de chantier »,
 * compteurs temporaires pour diagnostic volume / 429.
 */
@Injectable({ providedIn: 'root' })
export class ChantierLogisticsInventoryCoordinatorService {
  private readonly api = inject(ChantierLogisticsInventoryApiService);

  private inflight: Observable<ChantierLogisticsInventoryDto> | null = null;

  /** Dernier GET **terminé** avec succès (réponse JSON). */
  private lastSuccessfulFetchWallClockMs = 0;

  /**
   * Totaux **session** (tous les GET inventaire, toutes raisons).
   * - `skippedEffectMinInterval` : refus côté effet « changement de chantier » (données encore fraîches, hors Real sync).
   */
  readonly volume = {
    getIssued: 0,
    dedupedInflight: 0,
    skippedEffectMinInterval: 0,
    http429Responses: 0,
  };

  private cycleSnapshot: RealSyncInventoryCycleSnapshot = { getIssued: 0, dedupedInflight: 0 };

  /** Remet à zéro uniquement le snapshot **cycle Real sync** (une seule affectation — pas de doublon). */
  beginRealSyncCycle(): void {
    this.cycleSnapshot = { getIssued: 0, dedupedInflight: 0 };
  }

  /** Log de fin de cycle : `cycle` = real-sync uniquement ; `sessionTotals` = cumul global. */
  logRealSyncCycleVolume(chantierId: number): void {
    console.debug('[FrontierVolume] fin cycle Real sync', {
      chantierId,
      cycle: { ...this.cycleSnapshot } satisfies RealSyncInventoryCycleSnapshot,
      sessionTotals: { ...this.volume },
    });
  }

  logSessionVolume(context: string): void {
    console.debug(`[FrontierVolume] ${context}`, { ...this.volume });
  }

  /**
   * @param reason
   * - `effect` : changement de chantier (hors Real sync) — intervalle min + pas de spam. Émet `null` si données encore fraîches.
   * - `real-sync` : cycle automatique — pas d'intervalle min, seulement dédup in-flight.
   * - `manual` : après refresh manuel chantier — force l'appel (hors cooldown HTTP serveur).
   */
  getInventory$(reason: 'effect' | 'real-sync' | 'manual'): Observable<ChantierLogisticsInventoryDto | null> {
    const MIN_EFFECT_INTERVAL_MS = 35_000;

    if (this.inflight) {
      this.volume.dedupedInflight++;
      if (reason === 'real-sync') this.cycleSnapshot.dedupedInflight++;
      console.debug(
        `[FrontierVolume] inventaire GET fusionné (déjà en vol) reason=${reason} sessionDedup=${this.volume.dedupedInflight}`,
      );
      return this.inflight;
    }

    if (reason === 'effect') {
      const elapsed = Date.now() - this.lastSuccessfulFetchWallClockMs;
      if (this.lastSuccessfulFetchWallClockMs > 0 && elapsed < MIN_EFFECT_INTERVAL_MS) {
        this.volume.skippedEffectMinInterval++;
        console.debug(
          `[FrontierVolume] inventaire GET ignoré (effet chantier, données encore fraîches ${Math.round(elapsed / 1000)}s / min ${MIN_EFFECT_INTERVAL_MS / 1000}s)`,
        );
        return of(null);
      }
    }

    this.volume.getIssued++;
    if (reason === 'real-sync') this.cycleSnapshot.getIssued++;
    console.debug(
      `[FrontierVolume] inventaire GET émis reason=${reason} sessionIssued=${this.volume.getIssued}`,
    );

    this.inflight = this.api.getInventory().pipe(
      finalize(() => {
        this.inflight = null;
      }),
      shareReplay({ bufferSize: 1, refCount: true }),
    );
    return this.inflight;
  }

  /** À appeler après réponse 2xx parsée (pas sur erreur réseau avant parse). */
  markSuccessfulFetch(): void {
    this.lastSuccessfulFetchWallClockMs = Date.now();
  }

  recordHttp429(): void {
    this.volume.http429Responses++;
    console.debug(
      `[FrontierVolume] 429 inventaire session429=${this.volume.http429Responses}`,
    );
  }
}
