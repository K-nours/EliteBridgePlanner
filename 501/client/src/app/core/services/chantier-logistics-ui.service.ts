import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { ActiveChantiersStore, type ActiveChantierSite } from '../state/active-chantiers.store';

/**
 * Sélection d’un site de construction pour le panneau logistique + option « Real sync » (polling ciblé).
 */
@Injectable({ providedIn: 'root' })
export class ChantierLogisticsUiService {
  private readonly store = inject(ActiveChantiersStore);

  readonly selectedSiteId = signal<string | null>(null);

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
  }

  /** Polling automatique toutes les 5 min sur le chantier sélectionné uniquement (via refresh-one). OFF par défaut. */
  readonly realSyncEnabled = signal(false);

  /** ISO date/heure de la dernière synchro Real sync réussie. */
  readonly lastRealSyncSuccessAt = signal<string | null>(null);

  /** Erreur réseau / serveur pour le cycle Real sync (pas 401 — voir Frontier). */
  readonly realSyncError = signal<string | null>(null);

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

  setRealSyncEnabled(value: boolean): void {
    this.realSyncEnabled.set(value);
    if (!value) {
      this.realSyncError.set(null);
    }
  }

  touchRealSyncSuccess(): void {
    this.lastRealSyncSuccessAt.set(new Date().toISOString());
    this.realSyncError.set(null);
  }
}
