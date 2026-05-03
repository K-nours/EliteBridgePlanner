import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { ActiveChantiersStore, type ActiveChantierSite } from '../state/active-chantiers.store';
import type { ChantierLogisticsInventoryDto } from '../models/chantier-logistics-inventory.model';
import {
  readSliceForChantier,
  writeSliceForChantier,
  type ChantierLogisticsPersistedSlice,
} from './chantier-logistics-persistence';

/** Erreur métier : marketId enregistré ne renvoie pas de bloc chantier sur GET /market?marketId=. */
export interface ChantierMarketIdCapiIssueState {
  code: string;
  message: string;
  requiresRedock: boolean;
}

/**
 * Sélection d’un site de construction pour le panneau logistique + option « Real sync » (polling ciblé).
 * Real sync et horodatages sont **persistés par chantier** (localStorage) pour survivre au F5.
 */
@Injectable({ providedIn: 'root' })
export class ChantierLogisticsUiService {
  private readonly store = inject(ActiveChantiersStore);

  readonly selectedSiteId = signal<string | null>(null);

  /** Dernier id pour lequel on a restauré / persisté (évite de réécraser l’état à chaque refresh des listes). */
  private appliedPersistenceForId: string | null = null;

  constructor() {
    effect(() => {
      const entries = this.store.entries();
      const id = this.selectedSiteId();
      if (entries.length === 0) {
        if (id !== null) {
          this.selectedSiteId.set(null);
        }
        return;
      }
      const selectionOk = id != null && entries.some((e) => e.id === id);
      if (!selectionOk) {
        this.selectSite(entries[0]);
      }
    });

    effect(() => {
      const id = this.selectedSiteId();
      const entries = this.store.entries();
      if (id == null) {
        if (this.appliedPersistenceForId != null) {
          const prev = this.appliedPersistenceForId;
          this.appliedPersistenceForId = null;
          this.persistSliceForChantierId(prev);
        }
        this.resetMemoryWhenNoSelection();
        return;
      }
      if (!entries.some((e) => e.id === id)) {
        return;
      }
      if (id === this.appliedPersistenceForId) {
        return;
      }
      const prev = this.appliedPersistenceForId;
      this.appliedPersistenceForId = id;
      if (prev != null && prev !== id) {
        this.persistSliceForChantierId(prev);
      }
      this.restoreSliceForChantierId(id);
    });
  }

  /** Polling automatique toutes les 5 min sur le chantier sélectionné uniquement (via refresh-one). OFF par défaut. */
  readonly realSyncEnabled = signal(false);

  /**
   * Dernière mise à jour « générique » (refresh manuel réussi incluant rechargement listes).
   * @deprecated Préférer lastChantierNeedsVerifiedAt pour l’affichage des besoins CAPI.
   */
  readonly lastChantierDataRefreshSuccessAt = signal<string | null>(null);

  /**
   * Dernière fois où les besoins chantier ont été **vérifiés** avec succès via refresh-one + CAPI /market.
   * Non mis à jour si le refresh échoue (ex. marketId sans bloc chantier).
   */
  readonly lastChantierNeedsVerifiedAt = signal<string | null>(null);

  /** marketId stocké illisible côté CAPI — un simple retry ne suffit pas sans redock. */
  readonly chantierMarketIdCapiIssue = signal<ChantierMarketIdCapiIssueState | null>(null);

  /** ISO — dernière Real sync **réussie** (refresh-one + listes OK). */
  readonly lastRealSyncSuccessAt = signal<string | null>(null);

  /** ISO — fin de la dernière **tentative** Real sync (succès ou échec). */
  readonly lastRealSyncAttemptAt = signal<string | null>(null);

  readonly lastRealSyncAttemptOutcome = signal<'success' | 'failure' | null>(null);

  /** Échec refresh chantier (refresh-one / listes) — distinct auth / inventaire. */
  readonly realSyncChantierError = signal<string | null>(null);

  /** 401 Frontier sur le refresh chantier. */
  readonly realSyncAuthError = signal<string | null>(null);

  /** Dernier message CAPI pour le FC (succès partiel du cycle). */
  readonly realSyncCarrierCargoError = signal<string | null>(null);

  /** GET inventaire HTTP en échec alors que le refresh chantier a réussi. */
  readonly realSyncInventoryHttpError = signal<string | null>(null);

  /**
   * Dernier refresh-one / rechargement listes bloqué par rate limit API — la liste des chantiers peut être obsolète
   * (chantier terminé en jeu encore affiché jusqu’à un refresh valide).
   */
  readonly chantierServerListStaleDueToRateLimit = signal(false);

  /**
   * Après refresh 404 ou chantier terminé côté serveur : message unique (pas de métadonnées périmées).
   * Effacé dès qu’un autre site est sélectionné.
   */
  readonly chantierGoneMessage = signal<string | null>(null);

  readonly selectedSite = computed(() => {
    const id = this.selectedSiteId();
    if (!id) return null;
    return this.store.entries().find((e) => e.id === id) ?? null;
  });

  selectSite(site: ActiveChantierSite): void {
    this.chantierGoneMessage.set(null);
    this.selectedSiteId.set(site.id);
  }

  setChantierServerListStaleDueToRateLimit(stale: boolean): void {
    this.chantierServerListStaleDueToRateLimit.set(stale);
  }

  setRealSyncEnabled(value: boolean): void {
    this.realSyncEnabled.set(value);
    if (!value) {
      this.clearRealSyncErrorSignals();
      this.lastRealSyncAttemptAt.set(null);
      this.lastRealSyncAttemptOutcome.set(null);
    }
    this.persistCurrentChantierIfAny();
  }

  /** Après refresh manuel réussi uniquement (pas de cycle Real sync). */
  touchChantierDataRefreshSuccess(): void {
    const t = new Date().toISOString();
    this.lastChantierDataRefreshSuccessAt.set(t);
    this.lastChantierNeedsVerifiedAt.set(t);
    this.persistCurrentChantierIfAny();
  }

  setChantierMarketIdCapiIssue(state: ChantierMarketIdCapiIssueState | null): void {
    this.chantierMarketIdCapiIssue.set(state);
    this.persistCurrentChantierIfAny();
  }

  clearChantierMarketIdCapiIssue(): void {
    this.chantierMarketIdCapiIssue.set(null);
    this.persistCurrentChantierIfAny();
  }

  /**
   * Cycle Real sync : refresh chantier réussi + tentative inventaire terminée (DTO ou échec HTTP inventaire).
   * Ne pas utiliser pour un échec refresh chantier — voir `touchRealSyncChantierCycleFailure`.
   */
  touchRealSyncCycleSuccess(opts?: {
    inventory?: ChantierLogisticsInventoryDto | null;
    inventoryHttpFailed?: boolean;
    /** Cooldown client : pas d’appel inventaire ce cycle — ne pas effacer les signaux soute/FC. */
    inventorySkippedDueToCooldown?: boolean;
    /**
     * true si le refresh chantier n’a pas validé les besoins (marketId, skip refresh-one, etc.) :
     * ne pas mettre à jour les horodatages « données chantier à jour ».
     */
    skipChantierVerifiedTimestamp?: boolean;
  }): void {
    const t = new Date().toISOString();
    if (!opts?.skipChantierVerifiedTimestamp) {
      this.lastChantierDataRefreshSuccessAt.set(t);
      this.lastChantierNeedsVerifiedAt.set(t);
    }
    this.lastRealSyncSuccessAt.set(t);
    this.lastRealSyncAttemptAt.set(t);
    this.lastRealSyncAttemptOutcome.set('success');
    this.realSyncChantierError.set(null);
    this.realSyncAuthError.set(null);
    if (opts?.inventorySkippedDueToCooldown) {
      this.realSyncInventoryHttpError.set(null);
      this.persistCurrentChantierIfAny();
      return;
    }
    if (opts?.inventoryHttpFailed) {
      this.realSyncInventoryHttpError.set('Inventaire Frontier indisponible (réseau ou serveur)');
      this.realSyncCarrierCargoError.set(null);
    } else if (opts?.inventory) {
      this.realSyncInventoryHttpError.set(null);
      const inv = opts.inventory;
      const fc429 =
        inv.carrierRateLimited === true ||
        inv.carrierCargoError?.includes('429') === true ||
        inv.carrierCargoError?.includes('rate limit') === true;
      this.realSyncCarrierCargoError.set(fc429 ? null : inv.carrierCargoError);
    } else {
      this.realSyncInventoryHttpError.set(null);
      this.realSyncCarrierCargoError.set(null);
    }
    this.persistCurrentChantierIfAny();
  }

  /**
   * Rate limit HTTP 429 sur refresh chantier (API 501) : ne pas marquer l’échec du cycle,
   * garder Real sync actif et les données affichées.
   */
  touchRealSyncRateLimitNoFailure(): void {
    const t = new Date().toISOString();
    this.lastRealSyncAttemptAt.set(t);
    this.lastRealSyncAttemptOutcome.set('success');
    this.persistCurrentChantierIfAny();
  }

  /** Refresh chantier (ou auth) en échec — pas de succès « global » pour ce cycle. */
  touchRealSyncChantierCycleFailure(): void {
    const t = new Date().toISOString();
    this.lastRealSyncAttemptAt.set(t);
    this.lastRealSyncAttemptOutcome.set('failure');
    this.realSyncCarrierCargoError.set(null);
    this.realSyncInventoryHttpError.set(null);
    this.persistCurrentChantierIfAny();
  }

  /**
   * Inventaire mis à jour lors d'un cycle dont le refresh chantier a **échoué**.
   * Met à jour les signaux soute/FC et l'horodatage « Soute / FC » sans effacer l'ERREUR chantier
   * (ne modifie pas `lastRealSyncAttemptOutcome` ni `realSyncChantierError`).
   */
  touchRealSyncInventoryOnChantierError(inventory: ChantierLogisticsInventoryDto): void {
    this.lastRealSyncSuccessAt.set(new Date().toISOString());
    const fc429 =
      inventory.carrierRateLimited === true ||
      inventory.carrierCargoError?.includes('429') === true ||
      inventory.carrierCargoError?.includes('rate limit') === true;
    this.realSyncCarrierCargoError.set(fc429 ? null : (inventory.carrierCargoError ?? null));
    this.realSyncInventoryHttpError.set(null);
    this.persistCurrentChantierIfAny();
  }

  /** Changement de chantier ou chantier supprimé : réinitialiser les horodatages affichés. */
  clearChantierSyncTimestamps(): void {
    this.lastChantierDataRefreshSuccessAt.set(null);
    this.lastChantierNeedsVerifiedAt.set(null);
    this.lastRealSyncSuccessAt.set(null);
    this.lastRealSyncAttemptAt.set(null);
    this.lastRealSyncAttemptOutcome.set(null);
    this.chantierMarketIdCapiIssue.set(null);
    this.chantierServerListStaleDueToRateLimit.set(false);
    this.clearRealSyncErrorSignals();
    this.persistCurrentChantierIfAny();
  }

  private resetMemoryWhenNoSelection(): void {
    this.realSyncEnabled.set(false);
    this.lastChantierDataRefreshSuccessAt.set(null);
    this.lastChantierNeedsVerifiedAt.set(null);
    this.lastRealSyncSuccessAt.set(null);
    this.lastRealSyncAttemptAt.set(null);
    this.lastRealSyncAttemptOutcome.set(null);
    this.chantierMarketIdCapiIssue.set(null);
    this.chantierServerListStaleDueToRateLimit.set(false);
    this.clearRealSyncErrorSignals();
  }

  private clearRealSyncErrorSignals(): void {
    this.realSyncChantierError.set(null);
    this.realSyncAuthError.set(null);
    this.realSyncCarrierCargoError.set(null);
    this.realSyncInventoryHttpError.set(null);
  }

  private persistCurrentChantierIfAny(): void {
    const id = this.selectedSiteId();
    if (id != null) {
      this.persistSliceForChantierId(id);
    }
  }

  private persistSliceForChantierId(chantierId: string): void {
    const issue = this.chantierMarketIdCapiIssue();
    const slice: ChantierLogisticsPersistedSlice = {
      enabled: this.realSyncEnabled(),
      lastChantierDataRefreshSuccessAt: this.lastChantierDataRefreshSuccessAt(),
      lastChantierNeedsVerifiedAt: this.lastChantierNeedsVerifiedAt(),
      lastRealSyncSuccessAt: this.lastRealSyncSuccessAt(),
      lastRealSyncAttemptAt: this.lastRealSyncAttemptAt(),
      lastRealSyncAttemptOutcome: this.lastRealSyncAttemptOutcome(),
      chantierMarketIdIssueCode: issue?.code ?? null,
      chantierMarketIdIssueMessage: issue?.message ?? null,
      chantierMarketIdIssueRequiresRedock: issue?.requiresRedock ?? null,
    };
    writeSliceForChantier(chantierId, slice);
  }

  private restoreSliceForChantierId(chantierId: string): void {
    const s = readSliceForChantier(chantierId);
    if (s) {
      // Restaure la préférence Real sync par chantier (persistée en localStorage).
      this.realSyncEnabled.set(s.enabled ?? false);
      this.lastChantierDataRefreshSuccessAt.set(s.lastChantierDataRefreshSuccessAt);
      this.lastChantierNeedsVerifiedAt.set(s.lastChantierNeedsVerifiedAt ?? null);
      this.lastRealSyncSuccessAt.set(s.lastRealSyncSuccessAt);
      this.lastRealSyncAttemptAt.set(s.lastRealSyncAttemptAt);
      let outcome = s.lastRealSyncAttemptOutcome;
      const ta = s.lastRealSyncAttemptAt != null ? Date.parse(s.lastRealSyncAttemptAt) : NaN;
      const ts = s.lastRealSyncSuccessAt != null ? Date.parse(s.lastRealSyncSuccessAt) : NaN;
      if (outcome === 'failure' && !Number.isNaN(ta) && !Number.isNaN(ts) && ts >= ta) {
        outcome = 'success';
      }
      this.lastRealSyncAttemptOutcome.set(outcome);
      if (s.chantierMarketIdIssueCode) {
        this.chantierMarketIdCapiIssue.set({
          code: s.chantierMarketIdIssueCode,
          message: s.chantierMarketIdIssueMessage ?? '',
          requiresRedock: s.chantierMarketIdIssueRequiresRedock === true,
        });
      } else {
        this.chantierMarketIdCapiIssue.set(null);
      }
      this.clearRealSyncErrorSignals();
    } else {
      this.realSyncEnabled.set(false);
      this.lastChantierDataRefreshSuccessAt.set(null);
      this.lastChantierNeedsVerifiedAt.set(null);
      this.lastRealSyncSuccessAt.set(null);
      this.lastRealSyncAttemptAt.set(null);
      this.lastRealSyncAttemptOutcome.set(null);
      this.chantierMarketIdCapiIssue.set(null);
      this.clearRealSyncErrorSignals();
    }
    console.debug(
      `[RealSync] restore chantierId=${chantierId} enabled=${this.realSyncEnabled()} lastSyncAt=${this.lastRealSyncSuccessAt() ?? '—'} lastAttemptAt=${this.lastRealSyncAttemptAt() ?? '—'} outcome=${this.lastRealSyncAttemptOutcome() ?? '—'}`,
    );
  }
}
