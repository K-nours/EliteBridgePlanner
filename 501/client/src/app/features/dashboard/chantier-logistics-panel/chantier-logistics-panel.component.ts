import { Component, HostListener, computed, effect, inject, signal } from '@angular/core';
import { CommonModule, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import type { Observable } from 'rxjs';
import { catchError, forkJoin, map, of, switchMap, take } from 'rxjs';
import { ChantierLogisticsUiService } from '../../../core/services/chantier-logistics-ui.service';
import { ActiveChantiersStore } from '../../../core/state/active-chantiers.store';
import type { ConstructionResourceSnapshot } from '../../../core/state/active-chantiers.store';
import { DeclaredChantiersApiService } from '../../../core/services/declared-chantiers-api.service';
import { SyncLogService } from '../../../core/services/sync-log.service';
import { mapDeclaredListItemApiToSite } from '../../../core/utils/declared-chantiers-site-map';
import type { DeclaredChantierListItemApi } from '../../../core/models/declared-chantiers-api.model';

@Component({
  selector: 'app-chantier-logistics-panel',
  standalone: true,
  imports: [CommonModule, NgTemplateOutlet],
  templateUrl: './chantier-logistics-panel.component.html',
  styleUrl: './chantier-logistics-panel.component.scss',
})
export class ChantierLogisticsPanelComponent {
  protected readonly ui = inject(ChantierLogisticsUiService);
  private readonly chantiersStore = inject(ActiveChantiersStore);
  private readonly declaredApi = inject(DeclaredChantiersApiService);
  private readonly syncLog = inject(SyncLogService);

  protected readonly enlargeOpen = signal(false);
  protected readonly refreshLoading = signal(false);
  protected readonly deleteLoading = signal(false);

  constructor() {
    effect(() => {
      this.chantiersStore.entries();
      const id = this.ui.selectedSiteId();
      if (!id) return;
      if (!this.chantiersStore.entries().some((e) => e.id === id)) {
        this.ui.selectedSiteId.set(null);
      }
    });
  }

  /** Ressources avec restant &gt; 0, tri remaining décroissant. */
  protected readonly resourcesRemaining = computed(() => {
    const site = this.ui.selectedSite();
    if (!site) return [] as ConstructionResourceSnapshot[];
    const list = [...(site.constructionResources ?? [])].filter((r) => r.remaining > 0);
    list.sort((a, b) => b.remaining - a.remaining);
    return list;
  });

  protected readonly hasResourceData = computed(() => {
    const site = this.ui.selectedSite();
    if (!site) return false;
    const raw = site.constructionResources;
    return raw != null && raw.length > 0;
  });

  protected readonly allDelivered = computed(() => {
    const site = this.ui.selectedSite();
    if (!site || !this.hasResourceData()) return false;
    return this.resourcesRemaining().length === 0;
  });

  protected openEnlargeModal(event: Event): void {
    event.stopPropagation();
    this.enlargeOpen.set(true);
  }

  protected closeEnlargeModal(): void {
    this.enlargeOpen.set(false);
  }

  /** Recharge me + others depuis l’API et remplace le store (pas de merge). */
  private reloadChantierListsFromServer(): Observable<{
    mine: DeclaredChantierListItemApi[];
    others: DeclaredChantierListItemApi[];
  }> {
    return forkJoin({
      mine: this.declaredApi.listMine().pipe(catchError(() => of([] as DeclaredChantierListItemApi[]))),
      others: this.declaredApi.listOthers().pipe(catchError(() => of([] as DeclaredChantierListItemApi[]))),
    });
  }

  protected deleteSelected(event: Event): void {
    event.stopPropagation();
    const site = this.ui.selectedSite();
    if (!site || this.deleteLoading()) return;
    const id = Number(site.id);
    if (!Number.isFinite(id) || id <= 0) return;

    const ok = window.confirm(
      'Supprimer définitivement ce chantier en base de données ? Cette action est irréversible.',
    );
    if (!ok) return;

    console.log('DELETE chantier', id);
    const siteId = site.id;
    const mine = this.chantiersStore.mine().filter((e) => e.id !== siteId);
    const others = this.chantiersStore.others().filter((e) => e.id !== siteId);
    this.chantiersStore.replaceMineAndOthers(mine, others);
    this.ui.selectedSiteId.set(null);

    const stationLabel = site.stationName?.trim() || '—';
    this.deleteLoading.set(true);
    this.declaredApi
      .delete(id)
      .pipe(
        switchMap(() => this.reloadChantierListsFromServer()),
        map(({ mine: m, others: o }) => ({
          mine: m.map(mapDeclaredListItemApiToSite),
          others: o.map(mapDeclaredListItemApiToSite),
        })),
        take(1),
      )
      .subscribe({
        next: ({ mine: m, others: o }) => {
          this.chantiersStore.replaceMineAndOthers(m, o);
          this.deleteLoading.set(false);
          this.syncLog.addLog(`[Chantiers] chantier supprimé — ${stationLabel} (id ${id})`);
        },
        error: (err: unknown) => {
          this.reloadChantierListsFromServer()
            .pipe(
              map(({ mine: m, others: o }) => ({
                mine: m.map(mapDeclaredListItemApiToSite),
                others: o.map(mapDeclaredListItemApiToSite),
              })),
              take(1),
            )
            .subscribe((lists) => {
              this.chantiersStore.replaceMineAndOthers(lists.mine, lists.others);
            });
          this.deleteLoading.set(false);
          let msg = 'suppression impossible';
          if (err instanceof HttpErrorResponse) {
            const e = err.error as { message?: string } | undefined;
            if (typeof e?.message === 'string') msg = e.message;
            else if (err.status === 404) msg = 'chantier introuvable';
            else if (err.status === 401) msg = 'connexion Frontier requise';
          }
          this.syncLog.addLog(`[Chantiers] suppression échec — ${msg}`);
        },
      });
  }

  protected refreshSelected(event: Event): void {
    event.stopPropagation();
    const site = this.ui.selectedSite();
    if (!site || this.refreshLoading()) return;
    const id = Number(site.id);
    if (!Number.isFinite(id) || id <= 0) return;
    const stationLabel = site.stationName?.trim() || '—';
    this.refreshLoading.set(true);
    this.declaredApi
      .refreshOne(id)
      .pipe(
        switchMap((dto) =>
          this.reloadChantierListsFromServer().pipe(
            map(({ mine, others }) => {
              const prev = this.ui.selectedSiteId();
              this.chantiersStore.replaceMineAndOthers(
                mine.map(mapDeclaredListItemApiToSite),
                others.map(mapDeclaredListItemApiToSite),
              );
              if (prev && !dto.active) {
                this.ui.selectedSiteId.set(null);
                this.syncLog.addLog(`[Chantiers] chantier terminé — ${stationLabel} passé inactive`);
              } else if (dto.active) {
                this.syncLog.addLog(`[Chantiers] refresh manuel OK — ${stationLabel} — remaining updated`);
              }
              return dto;
            }),
          ),
        ),
        take(1),
      )
      .subscribe({
        next: () => this.refreshLoading.set(false),
        error: (err: unknown) => {
          this.refreshLoading.set(false);
          let msg = 'rafraîchissement impossible';
          if (err instanceof HttpErrorResponse) {
            const e = err.error as { message?: string } | undefined;
            if (typeof e?.message === 'string') msg = e.message;
            else if (err.status === 404) msg = 'chantier introuvable ou terminé';
          }
          this.syncLog.addLog(`[Chantiers] refresh échec — ${msg}`);
        },
      });
  }

  @HostListener('document:keydown.escape', ['$event'])
  protected onEscapeEnlarge(event: Event): void {
    if (!this.enlargeOpen()) return;
    event.preventDefault();
    this.closeEnlargeModal();
  }
}
