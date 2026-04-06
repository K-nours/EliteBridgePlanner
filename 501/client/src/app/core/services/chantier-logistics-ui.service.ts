import { Injectable, computed, inject, signal } from '@angular/core';
import { ActiveChantiersStore, type ActiveChantierSite } from '../state/active-chantiers.store';

/**
 * Sélection d’un site de construction pour le panneau logistique.
 */
@Injectable({ providedIn: 'root' })
export class ChantierLogisticsUiService {
  private readonly store = inject(ActiveChantiersStore);

  readonly selectedSiteId = signal<string | null>(null);

  readonly selectedSite = computed(() => {
    const id = this.selectedSiteId();
    if (!id) return null;
    return this.store.entries().find((e) => e.id === id) ?? null;
  });

  selectSite(site: ActiveChantierSite): void {
    this.selectedSiteId.set(site.id);
  }
}
