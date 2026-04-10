import { Component, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { CommonModule, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import type { Observable } from 'rxjs';
import { catchError, combineLatest, concatMap, EMPTY, finalize, forkJoin, map, of, switchMap, take, tap, throwError, timer } from 'rxjs';
import { ChantierLogisticsUiService } from '../../../core/services/chantier-logistics-ui.service';
import { ActiveChantiersStore } from '../../../core/state/active-chantiers.store';
import type { ConstructionResourceSnapshot } from '../../../core/state/active-chantiers.store';
import { DeclaredChantiersApiService } from '../../../core/services/declared-chantiers-api.service';
import { ChantierLogisticsInventoryApiService } from '../../../core/services/chantier-logistics-inventory-api.service';
import type { ChantierLogisticsInventoryDto } from '../../../core/models/chantier-logistics-inventory.model';
import { FrontierAuthService } from '../../../core/services/frontier-auth.service';
import { SyncLogService } from '../../../core/services/sync-log.service';
import { mapDeclaredListItemApiToSite } from '../../../core/utils/declared-chantiers-site-map';
import type { DeclaredChantierListItemApi } from '../../../core/models/declared-chantiers-api.model';
import {
  buildChantierResourceRows,
  buildGlobalNeedByCommodityMap,
  knownStockSum,
  showTotalColumn,
  splitStationDisplayLabel,
  type ChantierResourceRowVm,
  type InventoryTrust,
} from './chantier-logistics.vm';

/** Polling Real sync : premier tick immédiat puis toutes les 5 minutes (chantier sélectionné uniquement). */
const REAL_SYNC_INTERVAL_MS = 5 * 60 * 1000;

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
  private readonly inventoryApi = inject(ChantierLogisticsInventoryApiService);
  protected readonly frontierAuth = inject(FrontierAuthService);
  private readonly syncLog = inject(SyncLogService);

  protected readonly enlargeOpen = signal(false);
  protected readonly refreshLoading = signal(false);
  protected readonly realSyncTickRunning = signal(false);
  protected readonly deleteLoading = signal(false);

  /** Dernière réponse inventaire Frontier (soutes). */
  protected readonly inventoryPayload = signal<ChantierLogisticsInventoryDto | null>(null);
  protected readonly inventoryLoading = signal(false);
  protected readonly inventoryHttpError = signal(false);

  private prevSelectedSiteId: string | null = null;

  protected readonly canUseRealSyncSwitch = computed(
    () => this.ui.selectedSite() != null && !this.ui.chantierGoneMessage(),
  );

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

  /**
   * Stocks CAPI utilisables par colonne (— si erreur partielle / pas de payload).
   */
  protected readonly inventoryTrust = computed((): InventoryTrust => {
    const conn = this.frontierAuth.isConnected();
    const loading = this.inventoryLoading();
    const httpErr = this.inventoryHttpError();
    const inv = this.inventoryPayload();
    if (!conn || loading || httpErr || !inv) {
      return { shipKnown: false, carrierKnown: false };
    }
    return {
      shipKnown: !inv.shipCargoError,
      carrierKnown: !inv.carrierCargoError,
    };
  });

  /** Lignes besoin vs soute vaisseau / FC (calcul pur). */
  protected readonly resourceRows = computed((): ChantierResourceRowVm[] => {
    const site = this.ui.selectedSite();
    const inv = this.inventoryPayload();
    if (!site) return [];
    const mineActive = this.chantiersStore.mine().filter((e) => e.active);
    const globalNeedByCommodity = buildGlobalNeedByCommodityMap(mineActive);
    return buildChantierResourceRows(
      site.constructionResources,
      inv?.shipCargoByName ?? {},
      inv?.carrierCargoByName ?? {},
      this.inventoryTrust(),
      globalNeedByCommodity,
    );
  });

  /** Colorer les lignes seulement quand au moins un stock est connu. */
  protected readonly inventoryRowHasStockInfo = computed(
    () => this.inventoryTrust().shipKnown || this.inventoryTrust().carrierKnown,
  );

  protected readonly knownStockSum = knownStockSum;
  protected readonly showTotalColumn = showTotalColumn;
  protected readonly splitStationDisplayLabel = splitStationDisplayLabel;

  constructor() {
    effect(() => {
      const id = this.ui.selectedSiteId();
      if (id !== this.prevSelectedSiteId) {
        this.prevSelectedSiteId = id;
        this.ui.lastRealSyncSuccessAt.set(null);
        this.ui.realSyncError.set(null);
      }
    });

    effect(
      () => {
        const site = this.ui.selectedSite();
        const gone = this.ui.chantierGoneMessage();
        const connected = this.frontierAuth.isConnected();
        if (gone || !site) {
          untracked(() => {
            this.inventoryPayload.set(null);
            this.inventoryHttpError.set(false);
            this.inventoryLoading.set(false);
          });
          return;
        }
        if (!connected) {
          untracked(() => {
            this.inventoryPayload.set(null);
            this.inventoryHttpError.set(false);
          });
          return;
        }
        untracked(() => this.fetchPlayerInventory());
      },
      { allowSignalWrites: true },
    );

    combineLatest([
      toObservable(this.ui.realSyncEnabled),
      toObservable(this.ui.selectedSiteId),
      toObservable(this.frontierAuth.isConnected),
      toObservable(this.ui.chantierGoneMessage),
    ])
      .pipe(
        switchMap(([enabled, siteId, connected, gone]) => {
          if (!enabled || !siteId || !connected || gone) {
            return EMPTY;
          }
          const id = Number(siteId);
          if (!Number.isFinite(id) || id <= 0) {
            return EMPTY;
          }
          return timer(0, REAL_SYNC_INTERVAL_MS).pipe(
            concatMap(() => {
              const site = this.ui.selectedSite();
              const sid = site ? Number(site.id) : NaN;
              if (!site || sid !== id) {
                return EMPTY;
              }
              const stationLabel = site.stationName?.trim() || '—';
              this.realSyncTickRunning.set(true);
              return this.runRealSyncRefresh(id, stationLabel).pipe(
                finalize(() => this.realSyncTickRunning.set(false)),
              );
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  protected onRealSyncToggle(checked: boolean): void {
    this.ui.setRealSyncEnabled(checked);
  }

  /** Libellé relatif pour le bandeau header. */
  protected formatLastSyncLabel(iso: string | null): string {
    if (!iso) return '—';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '—';
    const diffMs = Date.now() - d.getTime();
    if (diffMs < 90_000) return "à l'instant";
    const mins = Math.floor(diffMs / 60_000);
    if (mins < 120) return `il y a ${mins} min`;
    return d.toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
  }

  private runRealSyncRefresh(chantierId: number, stationLabel: string): Observable<void> {
    this.ui.realSyncError.set(null);
    return this.refreshChantierPipeline(chantierId, stationLabel, 'realSync').pipe(
      tap(() => this.ui.touchRealSyncSuccess()),
      catchError((err: unknown) => {
        this.applyRealSyncHttpError(err);
        return of(void 0);
      }),
    );
  }

  private applyRealSyncHttpError(err: unknown): void {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 401) {
        this.ui.realSyncError.set('Session expirée — reconnectez Frontier');
        return;
      }
      const e = err.error as { message?: string } | undefined;
      let msg = typeof e?.message === 'string' ? e.message : 'synchro impossible';
      if (err.status === 404) msg = 'chantier introuvable ou terminé';
      this.ui.realSyncError.set(msg);
      return;
    }
    this.ui.realSyncError.set('erreur réseau');
  }

  private reloadChantierListsFromServer(): Observable<{
    mine: DeclaredChantierListItemApi[];
    others: DeclaredChantierListItemApi[];
  }> {
    return forkJoin({
      mine: this.declaredApi.listMine().pipe(catchError(() => of([] as DeclaredChantierListItemApi[]))),
      others: this.declaredApi.listOthers().pipe(catchError(() => of([] as DeclaredChantierListItemApi[]))),
    });
  }

  private applyChantierGoneState(mode: 'manual' | 'realSync'): void {
    this.ui.selectedSiteId.set(null);
    this.ui.setRealSyncEnabled(false);
    this.ui.lastRealSyncSuccessAt.set(null);
    this.ui.realSyncError.set(null);
    this.ui.chantierGoneMessage.set('Chantier introuvable ou terminé');
    this.inventoryPayload.set(null);
    this.inventoryHttpError.set(false);
    if (mode === 'manual') {
      this.syncLog.addLog('[Chantiers] chantier introuvable ou terminé — sélection effacée, Real sync désactivé');
    } else {
      this.syncLog.addLog('[Chantiers] Real sync — chantier introuvable ou terminé — polling arrêté');
    }
  }

  private fetchPlayerInventory(): void {
    if (!this.frontierAuth.isConnected()) return;
    this.inventoryLoading.set(true);
    this.inventoryHttpError.set(false);
    this.inventoryApi
      .getInventory()
      .pipe(
        take(1),
        catchError(() => {
          this.inventoryHttpError.set(true);
          this.inventoryPayload.set(null);
          return of(null);
        }),
        finalize(() => this.inventoryLoading.set(false)),
      )
      .subscribe((dto) => {
        if (dto) {
          this.inventoryPayload.set(dto);
          this.inventoryHttpError.set(false);
        }
      });
  }

  private refreshChantierPipeline(id: number, stationLabel: string, mode: 'manual' | 'realSync'): Observable<void> {
    return this.declaredApi.refreshOne(id).pipe(
      catchError((err: unknown) => {
        if (err instanceof HttpErrorResponse && err.status === 404) {
          return this.reloadChantierListsFromServer().pipe(
            tap(({ mine, others }) => {
              this.chantiersStore.replaceMineAndOthers(
                mine.map(mapDeclaredListItemApiToSite),
                others.map(mapDeclaredListItemApiToSite),
              );
              this.applyChantierGoneState(mode);
            }),
            switchMap(() => EMPTY),
          );
        }
        return throwError(() => err);
      }),
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
              if (mode === 'manual') {
                this.syncLog.addLog(`[Chantiers] chantier terminé — ${stationLabel} passé inactive`);
              } else {
                this.syncLog.addLog(`[Chantiers] Real sync — chantier terminé — ${stationLabel}`);
              }
            } else if (dto.active) {
              if (mode === 'manual') {
                this.syncLog.addLog(`[Chantiers] refresh manuel OK — ${stationLabel} — remaining updated`);
              } else {
                this.syncLog.addLog(`[Chantiers] Real sync OK — ${stationLabel}`);
              }
            }
            return dto;
          }),
        ),
      ),
      tap(() => untracked(() => this.fetchPlayerInventory())),
      map(() => void 0),
      take(1),
    );
  }

  protected openEnlargeModal(event: Event): void {
    event.stopPropagation();
    this.enlargeOpen.set(true);
  }

  protected closeEnlargeModal(): void {
    this.enlargeOpen.set(false);
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
    this.refreshChantierPipeline(id, stationLabel, 'manual')
      .pipe(
        catchError((err: unknown) => {
          let msg = 'rafraîchissement impossible';
          if (err instanceof HttpErrorResponse) {
            const e = err.error as { message?: string } | undefined;
            if (typeof e?.message === 'string') msg = e.message;
            else if (err.status === 404) msg = 'chantier introuvable ou terminé';
          }
          this.syncLog.addLog(`[Chantiers] refresh échec — ${msg}`);
          return EMPTY;
        }),
        finalize(() => this.refreshLoading.set(false)),
      )
      .subscribe();
  }

  @HostListener('document:keydown.escape', ['$event'])
  protected onEscapeEnlarge(event: Event): void {
    if (!this.enlargeOpen()) return;
    event.preventDefault();
    this.closeEnlargeModal();
  }
}
