import { Component, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { CommonModule, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import type { Observable } from 'rxjs';
import {
  catchError,
  combineLatest,
  concatMap,
  EMPTY,
  finalize,
  forkJoin,
  map,
  mergeMap,
  of,
  switchMap,
  take,
  tap,
  throwError,
  timer,
} from 'rxjs';
import { ChantierLogisticsUiService } from '../../../core/services/chantier-logistics-ui.service';
import { ActiveChantiersStore } from '../../../core/state/active-chantiers.store';
import type { ConstructionResourceSnapshot } from '../../../core/state/active-chantiers.store';
import { DeclaredChantiersApiService } from '../../../core/services/declared-chantiers-api.service';
import { ChantierLogisticsInventoryCoordinatorService } from '../../../core/services/chantier-logistics-inventory-coordinator.service';
import type { ChantierLogisticsInventoryDto } from '../../../core/models/chantier-logistics-inventory.model';
import { FrontierAuthService } from '../../../core/services/frontier-auth.service';
import { SyncLogService } from '../../../core/services/sync-log.service';
import { mapDeclaredListItemApiToSite } from '../../../core/utils/declared-chantiers-site-map';
import type { DeclaredChantierListItemApi } from '../../../core/models/declared-chantiers-api.model';
import { CHANTIER_REFRESH_ERROR_MARKET_NO_CAPI_BLOCK } from '../../../core/constants/chantier-declared-errors';
import { ChantierMarketRefreshCooldownService } from '../../../core/services/chantier-market-refresh-cooldown.service';
import { FrontierCapiRateLimitService } from '../../../core/services/frontier-capi-rate-limit.service';
import {
  buildChantierResourceRows,
  buildGlobalNeedByCommodityMap,
  computeInventoryTrust,
  dedupeChantierSitesById,
  knownStockSum,
  logGlobalRequirementsRawByChantier,
  logInventoryMappingDebug,
  logShipCargoPayloadDiagnostic,
  mergeInventoryDtos,
  showTotalColumn,
  splitStationDisplayLabel,
  type ChantierResourceRowVm,
  type InventoryTrust,
} from './chantier-logistics.vm';

/** Polling Real sync : premier tick immédiat puis toutes les 5 minutes (chantier sélectionné uniquement). */
const REAL_SYNC_INTERVAL_MS = 5 * 60 * 1000;

function parseRetryAfterSeconds(err: HttpErrorResponse): number | null {
  const h = err.headers?.get('Retry-After');
  if (h != null && /^\d+$/.test(h.trim())) return parseInt(h.trim(), 10);
  return null;
}

/** Marge après l’intervalle avant d’afficher « en retard » (évite faux positifs si le tick est légèrement en retard). */
const REAL_SYNC_LATE_GRACE_MS = 60_000;

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
  private readonly inventoryCoordinator = inject(ChantierLogisticsInventoryCoordinatorService);
  protected readonly frontierAuth = inject(FrontierAuthService);
  private readonly syncLog = inject(SyncLogService);
  private readonly capiRateLimit = inject(FrontierCapiRateLimitService);
  private readonly chantierMarketCooldown = inject(ChantierMarketRefreshCooldownService);

  /** Dernier GET inventaire : échec HTTP 429 (pour ne pas afficher « erreur réseau » générique). */
  private lastInventoryFetchWasHttp429 = false;

  /**
   * true si refresh-one a rechargé les listes avec succès (besoins CAPI fiables pour ce cycle).
   * Sinon (marketId bloquant, cooldown refresh-one) → Real sync ne met pas à jour l’horodatage « besoins vérifiés ».
   */
  private chantierRefreshVerifiedThisCycle = false;

  protected readonly enlargeOpen = signal(false);
  protected readonly refreshLoading = signal(false);
  protected readonly realSyncTickRunning = signal(false);
  protected readonly deleteLoading = signal(false);

  /** Dernière réponse inventaire Frontier (soutes). */
  protected readonly inventoryPayload = signal<ChantierLogisticsInventoryDto | null>(null);
  protected readonly inventoryLoading = signal(false);
  protected readonly inventoryHttpError = signal(false);

  /** Pour ne logger qu’une transition Frontier connecté → déconnecté (panneau sync, pas le pavé logistique). */
  private frontierConnInitialized = false;
  private frontierWasConnected = true;

  /** Ignore les réponses inventaire obsolètes (courses concurrentes Real sync / effet). */
  private inventoryFetchGeneration = 0;

  /** Évite de spammer la console : log quand les besoins ou le chantier changent réellement. */
  private prevLogisticsDataSig: string | null = null;

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
   * Stocks CAPI utilisables par colonne.
   * Ne pas masquer soute/FC pendant un simple re-fetch : `loading` ne doit pas tout passer à « — »
   * (sinon activation Real sync + refresh parait « vider » le FC).
   */
  protected readonly inventoryTrust = computed((): InventoryTrust =>
    computeInventoryTrust(this.frontierAuth.isConnected(), this.inventoryHttpError(), this.inventoryPayload()),
  );

  /** Lignes besoin vs soute vaisseau / FC (calcul pur). */
  protected readonly resourceRows = computed((): ChantierResourceRowVm[] => {
    const site = this.ui.selectedSite();
    if (!site) return [];
    const inv = this.inventoryPayload();
    /** Copie défensive des lignes besoin pour éviter toute référence partagée entre chantiers. */
    const needs = site.constructionResources?.map((r) => ({ ...r })) ?? [];
    const mineActive = dedupeChantierSitesById(this.chantiersStore.mine().filter((e) => e.active));
    const globalNeedByCommodity = buildGlobalNeedByCommodityMap(mineActive);
    return buildChantierResourceRows(
      needs,
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
      const site = this.ui.selectedSite();
      const inv = this.inventoryPayload();
      const trust = this.inventoryTrust();
      const mine = dedupeChantierSitesById(this.chantiersStore.mine().filter((e) => e.active));
      const globalMap = buildGlobalNeedByCommodityMap(mine);
      if (!site) return;
      const needsSig = JSON.stringify(
        site.constructionResources?.map((r) => ({ name: r.name, remaining: r.remaining })) ?? [],
      );
      const sig = `${site.id}|${site.marketId ?? ''}|${needsSig}|${inv?.fetchedAtUtc ?? ''}`;
      if (sig === this.prevLogisticsDataSig) {
        return;
      }
      this.prevLogisticsDataSig = sig;
      console.debug('[Logistics] selected chantier:', {
        chantierId: site.id,
        siteName: site.stationName,
        marketId: site.marketId,
      });
      console.debug('[Logistics] selected requirements raw:', {
        requirements: site.constructionResources?.map((r) => ({ name: r.name, remaining: r.remaining })),
      });
      logGlobalRequirementsRawByChantier(mine);
      console.debug('[Logistics] global requirements aggregate:', Object.fromEntries(globalMap.entries()));
      logInventoryMappingDebug(site.stationName ?? '—', site.constructionResources, inv, trust);
      console.debug('[Logistics] stocks (merged payload):', {
        shipStockMapped: inv?.shipCargoByName ?? {},
        fcStockMapped: inv?.carrierCargoByName ?? {},
      });
    });

    effect(() => {
      const conn = this.frontierAuth.isConnected();
      const rsOn = this.ui.realSyncEnabled();
      untracked(() => {
        if (!this.frontierConnInitialized) {
          this.frontierConnInitialized = true;
          this.frontierWasConnected = conn;
          return;
        }
        if (this.frontierWasConnected && !conn && rsOn) {
          const site = this.ui.selectedSite();
          const ctx = site ? this.chantierCtx(site.id, site.stationName) : 'id=— · «—»';
          this.syncLog.addLog(`[Chantiers] ${ctx} · Real sync — Frontier déconnecté, synchro automatique suspendue`);
        }
        this.frontierWasConnected = conn;
      });
    });

    /**
     * Inventaire joueur (soute / FC) : hors Real sync uniquement.
     * Avec Real sync actif, le polling enchaîne déjà refresh-one + GET inventaire — évite doublons au changement de chantier.
     * Debounce 450 ms pour limiter les rafales si la sélection change vite.
     */
    effect(
      (onCleanup) => {
        const site = this.ui.selectedSite();
        const gone = this.ui.chantierGoneMessage();
        const connected = this.frontierAuth.isConnected();
        const tickRunning = this.realSyncTickRunning();
        const rsOn = this.ui.realSyncEnabled();
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
        if (tickRunning) {
          return;
        }
        if (rsOn) {
          console.debug(
            '[FrontierVolume] effet inventaire ignoré — Real sync actif (pas de GET doublon au changement de chantier)',
          );
          return;
        }
        if (!this.capiRateLimit.canFetchInventory()) {
          console.debug(
            `[FrontierVolume] inventaire effet ignoré — cooldown HTTP (${this.capiRateLimit.secondsUntilAllowed()}s)`,
          );
          return;
        }
        const siteId = site.id;
        const tid = window.setTimeout(() => {
          untracked(() => {
            if (this.ui.selectedSite()?.id !== siteId) return;
            this.fetchPlayerInventoryFromEffect();
          });
        }, 450);
        onCleanup(() => clearTimeout(tid));
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
        tap(([enabled, siteId, connected, gone]) => {
          if (enabled && siteId && connected && !gone) {
            console.debug('[FrontierVolume] Real sync combineLatest — (re)bind polling', {
              siteId,
              sessionInventoryStats: { ...this.inventoryCoordinator.volume },
            });
          }
        }),
        switchMap(([enabled, siteId, connected, gone]) => {
          if (!enabled || !siteId || !connected || gone) {
            return EMPTY;
          }
          const id = Number(siteId);
          if (!Number.isFinite(id) || id <= 0) {
            return EMPTY;
          }
          this.realSyncLog(
            `started polling chantierId=${id} intervalMs=${REAL_SYNC_INTERVAL_MS} (restauré ou activé)`,
          );
          return timer(0, REAL_SYNC_INTERVAL_MS).pipe(
            tap((tickIndex) =>
              this.realSyncLog(`tick #${tickIndex} at ${new Date().toISOString()} chantierId=${id}`),
            ),
            finalize(() => this.realSyncLog(`timer stopped for chantier ${id}`)),
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
    const sid = this.ui.selectedSiteId();
    if (checked) {
      this.realSyncLog(`started for chantier ${sid}`);
      if (!this.frontierAuth.isConnected()) {
        const site = this.ui.selectedSite();
        const ctx = site ? this.chantierCtx(site.id, site.stationName) : `id=${sid ?? '—'} · «—»`;
        this.syncLog.addLog(`[Chantiers] ${ctx} · Real sync activé — Frontier hors ligne, synchro suspendue jusqu’à reconnexion`);
      }
    } else {
      this.realSyncLog(`stopped for chantier ${sid}`);
    }
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

  /**
   * Pilule Real sync : ne pas afficher « OK » si la synchro est en échec, en retard ou hors ligne.
   */
  protected readonly realSyncPillState = computed(
    (): 'offline' | 'off' | 'error' | 'chantierwarn' | 'partial' | 'pending' | 'ratelimit' | 'late' | 'ok' => {
      if (!this.ui.realSyncEnabled()) return 'off';
      if (!this.frontierAuth.isConnected()) return 'offline';
      if (this.ui.realSyncAuthError() != null || this.ui.realSyncChantierError() != null) return 'error';
      if (this.ui.lastRealSyncAttemptOutcome() === 'failure') return 'error';
      if (this.realSyncTickRunning()) return 'pending';
      if (this.capiRateLimit.isInCooldown()) return 'ratelimit';
      /** Besoins chantier non vérifiables (marketId) alors que le cycle peut quand même mettre à jour l’inventaire. */
      if (this.ui.chantierMarketIdCapiIssue() != null) return 'chantierwarn';
      if (
        this.ui.realSyncInventoryHttpError() != null ||
        this.ui.realSyncShipCargoError() != null ||
        this.ui.realSyncCarrierCargoError() != null
      ) {
        return 'partial';
      }
      const successAt = this.ui.lastRealSyncSuccessAt();
      if (this.isRealSyncSuccessLate(successAt)) return 'late';
      return 'ok';
    },
  );

  protected readonly realSyncPillLabel = computed(() => {
    switch (this.realSyncPillState()) {
      case 'off':
        return 'Inactif';
      case 'offline':
        return 'Hors ligne';
      case 'error':
        return 'Erreur';
      case 'chantierwarn':
        return 'Besoins ?';
      case 'partial':
        return 'Partiel';
      case 'pending':
        return '…';
      case 'ratelimit':
        return 'Limite API';
      case 'late':
        return 'Retard';
      case 'ok':
        return 'OK';
      default:
        return '—';
    }
  });

  /** Infobulle : état agrégé + détail des sous-parties (chantier / auth / inventaire). */
  protected readonly realSyncButtonTitle = computed(() => {
    if (!this.ui.realSyncEnabled()) {
      return 'Real sync inactif — clic pour activer (toutes les 5 min)';
    }
    const parts: string[] = [];
    const auth = this.ui.realSyncAuthError();
    const ch = this.ui.realSyncChantierError();
    const marketIssue = this.ui.chantierMarketIdCapiIssue();
    const invHttp = this.ui.realSyncInventoryHttpError();
    const ship = this.ui.realSyncShipCargoError();
    const fc = this.ui.realSyncCarrierCargoError();
    if (auth) parts.push(`Auth Frontier : ${auth}`);
    if (ch) parts.push(`Chantier : ${ch}`);
    if (marketIssue) {
      parts.push(
        `Besoins chantier : marketId invalide ou périmé — redock pour réenregistrer (${marketIssue.code})`,
      );
    }
    if (invHttp) parts.push(`Inventaire HTTP : ${invHttp}`);
    if (ship) parts.push(`Soute CAPI : ${ship}`);
    if (fc) parts.push(`FC CAPI : ${fc}`);
    const sec = this.capiRateLimit.secondsUntilAllowed();
    if (sec > 0) {
      parts.unshift(`Rate limit Frontier — nouvelle tentative dans ${sec} s`);
    }
    const detail = parts.length ? ` — ${parts.join(' · ')}` : '';
    return `Real sync — ${this.realSyncPillLabel()}${detail} — clic pour désactiver`;
  });

  private isRealSyncSuccessLate(successIso: string | null): boolean {
    if (!successIso) return true;
    const t = new Date(successIso).getTime();
    if (Number.isNaN(t)) return true;
    return Date.now() - t > REAL_SYNC_INTERVAL_MS + REAL_SYNC_LATE_GRACE_MS;
  }

  private runRealSyncRefresh(chantierId: number, stationLabel: string): Observable<void> {
    this.chantierRefreshVerifiedThisCycle = false;
    this.inventoryCoordinator.beginRealSyncCycle();
    console.debug(`[RealSync] cycle start chantierId=${chantierId}`);
    return this.refreshChantierPipeline(chantierId, stationLabel, 'realSync').pipe(
      tap(() => {
        if (this.chantierRefreshVerifiedThisCycle) {
          console.debug(`[RealSync] besoins chantier vérifiés ce cycle chantierId=${chantierId}`);
        } else {
          console.debug(
            `[RealSync] besoins chantier non vérifiés (cooldown refresh-one ou marketId) chantierId=${chantierId}`,
          );
        }
      }),
      switchMap(() => {
        const skipChantierTs = !this.chantierRefreshVerifiedThisCycle;
        if (!this.capiRateLimit.canFetchInventory()) {
          const left = this.capiRateLimit.secondsUntilAllowed();
          console.debug(`[RealSync] skipping call due to cooldown (${left}s left)`);
          this.ui.touchRealSyncCycleSuccess({
            inventorySkippedDueToCooldown: true,
            skipChantierVerifiedTimestamp: skipChantierTs,
          });
          console.debug(`[RealSync] cycle success final (inventory skipped — cooldown) chantierId=${chantierId}`);
          return of(void 0);
        }
        return this.fetchPlayerInventoryForRealSync$().pipe(
          tap((dto) => {
            if (dto) {
              const shipOk = !dto.shipCargoError;
              const fcOk = !dto.carrierCargoError;
              console.debug(
                `[RealSync] ship cargo ${shipOk ? 'success' : 'fail'} chantierId=${chantierId}`,
                dto.shipCargoError ?? '',
              );
              console.debug(
                `[RealSync] FC cargo ${fcOk ? 'success' : 'fail'} chantierId=${chantierId}`,
                dto.carrierCargoError ?? '',
              );
            } else {
              console.debug(`[RealSync] inventory HTTP fail chantierId=${chantierId} (cache soute/FC conservé)`);
            }
            this.ui.touchRealSyncCycleSuccess({
              inventory: dto,
              inventoryHttpFailed: dto == null && !this.lastInventoryFetchWasHttp429,
              skipChantierVerifiedTimestamp: skipChantierTs,
            });
            this.lastInventoryFetchWasHttp429 = false;
            console.debug(`[RealSync] cycle success final chantierId=${chantierId}`);
          }),
          map(() => void 0),
        );
      }),
      catchError((err: unknown) => {
        if (err instanceof HttpErrorResponse && err.status === 429) {
          console.debug('[RealSync] HTTP 429 received (refresh chantier)');
          this.applyRealSyncHttpError(err, chantierId, stationLabel);
          this.ui.touchRealSyncRateLimitNoFailure();
          console.debug(`[RealSync] cycle end (rate limit chantier, pas d’échec Real sync) chantierId=${chantierId}`);
          return of(void 0);
        }
        const reason =
          err instanceof HttpErrorResponse
            ? `HTTP ${err.status} ${typeof err.error === 'object' && err.error && 'message' in err.error ? String((err.error as { message?: string }).message) : ''}`
            : String(err);
        console.debug(`[RealSync] chantier refresh fail chantierId=${chantierId} reason=${reason}`);
        this.applyRealSyncHttpError(err, chantierId, stationLabel);
        this.ui.touchRealSyncChantierCycleFailure();
        console.debug(`[RealSync] cycle fail final chantierId=${chantierId}`);
        return of(void 0);
      }),
      finalize(() => {
        this.inventoryCoordinator.logRealSyncCycleVolume(chantierId);
        console.debug(`[RealSync] cycle end chantierId=${chantierId}`);
      }),
    );
  }

  /** Inventaire dans le même cycle Real sync — mutualisé avec l’effet via le coordinateur (dédup in-flight). */
  private fetchPlayerInventoryForRealSync$(): Observable<ChantierLogisticsInventoryDto | null> {
    const gen = ++this.inventoryFetchGeneration;
    this.lastInventoryFetchWasHttp429 = false;
    this.inventoryLoading.set(true);
    this.inventoryHttpError.set(false);
    return this.inventoryCoordinator.getInventory$('real-sync').pipe(
      take(1),
      mergeMap((dto) => {
        if (dto === null) return of(null);
        return of(mergeInventoryDtos(this.inventoryPayload(), dto));
      }),
      tap((merged) => {
        if (merged == null) return;
        if (gen !== this.inventoryFetchGeneration) return;
        this.inventoryPayload.set(merged);
        this.inventoryHttpError.set(false);
        logShipCargoPayloadDiagnostic(merged);
        if (!merged.rateLimited) {
          this.inventoryCoordinator.markSuccessfulFetch();
        }
        if (merged.rateLimited) {
          this.capiRateLimit.record429(merged.retryAfterSeconds ?? null);
        } else {
          this.capiRateLimit.recordSuccess();
        }
      }),
      map((merged) => merged),
      catchError((err: unknown) => {
        if (err instanceof HttpErrorResponse && err.status === 429) {
          console.debug('[RealSync] HTTP 429 received (GET inventaire)');
          this.lastInventoryFetchWasHttp429 = true;
          this.inventoryCoordinator.recordHttp429();
          const ra = parseRetryAfterSeconds(err);
          this.capiRateLimit.record429(ra);
          if (gen === this.inventoryFetchGeneration) {
            this.inventoryHttpError.set(false);
          }
          return of(null);
        }
        if (gen === this.inventoryFetchGeneration) {
          this.inventoryHttpError.set(true);
        }
        return of(null);
      }),
      finalize(() => {
        if (gen === this.inventoryFetchGeneration) {
          this.inventoryLoading.set(false);
        }
      }),
    );
  }

  /** Logs temporaires de diagnostic Real sync — retirer ou brancher sur SyncLogService une fois stabilisé. */
  private realSyncLog(message: string): void {
    console.debug(`[RealSync] ${message}`);
  }

  private applyRealSyncHttpError(err: unknown, chantierId: number, stationLabel: string): void {
    const ctx = this.chantierCtx(chantierId, stationLabel);
    if (err instanceof HttpErrorResponse) {
      if (err.status === 429) {
        console.debug('[RealSync] HTTP 429 received (refresh chantier)');
        this.capiRateLimit.record429(parseRetryAfterSeconds(err));
        this.ui.realSyncChantierError.set(null);
        this.ui.setChantierServerListStaleDueToRateLimit(true);
        this.syncLog.addLog(`[Chantiers] ${ctx} · Real sync — rate limit API, nouvel essai après délai`);
        return;
      }
      if (err.status === 401) {
        this.ui.realSyncAuthError.set('Session expirée — reconnectez Frontier');
        this.ui.realSyncChantierError.set(null);
        this.syncLog.addLog(`[Chantiers] ${ctx} · Real sync — session expirée, reconnectez Frontier`);
        return;
      }
      const e = err.error as { message?: string } | undefined;
      let msg = typeof e?.message === 'string' ? e.message : 'synchro impossible';
      if (err.status === 404) msg = 'chantier introuvable ou terminé';
      this.ui.realSyncChantierError.set(msg);
      this.ui.realSyncAuthError.set(null);
      this.syncLog.addLog(`[Chantiers] ${ctx} · Real sync — erreur chantier : ${msg}`);
      return;
    }
    this.ui.realSyncChantierError.set('erreur réseau');
    this.ui.realSyncAuthError.set(null);
    this.syncLog.addLog(`[Chantiers] ${ctx} · Real sync — erreur réseau (chantier)`);
  }

  /** Tout log lié à un chantier précis doit inclure id + nom de site. */
  private chantierCtx(id: number | string, stationName: string | null | undefined): string {
    const n = (stationName ?? '—').trim().replace(/\s+/g, ' ') || '—';
    return `id=${id} · «${n}»`;
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

  private applyChantierGoneState(mode: 'manual' | 'realSync', chantierId: number, stationLabel: string): void {
    const ctx = this.chantierCtx(chantierId, stationLabel);
    /** Mettre à jour l’état / persistance avant de vider la sélection (sinon mauvaise slice en localStorage). */
    this.ui.setRealSyncEnabled(false);
    this.ui.clearChantierSyncTimestamps();
    this.ui.selectedSiteId.set(null);
    this.ui.chantierGoneMessage.set('Chantier introuvable ou terminé');
    this.inventoryPayload.set(null);
    this.inventoryHttpError.set(false);
    if (mode === 'manual') {
      this.syncLog.addLog(`[Chantiers] ${ctx} · introuvable ou terminé — sélection effacée, Real sync désactivé`);
    } else {
      this.syncLog.addLog(`[Chantiers] ${ctx} · Real sync — introuvable ou terminé — polling arrêté`);
    }
  }

  /** GET inventaire déclenché par l’effet (Real sync désactivé) — intervalle min + dédup via coordinateur. */
  private fetchPlayerInventoryFromEffect(): void {
    if (!this.frontierAuth.isConnected()) return;
    if (!this.capiRateLimit.canFetchInventory()) {
      console.debug(
        `[FrontierVolume] inventaire effet ignoré — cooldown HTTP (${this.capiRateLimit.secondsUntilAllowed()}s)`,
      );
      return;
    }
    const gen = ++this.inventoryFetchGeneration;
    this.inventoryLoading.set(true);
    this.inventoryHttpError.set(false);
    this.inventoryCoordinator
      .getInventory$('effect')
      .pipe(
        take(1),
        mergeMap((dto) => {
          if (dto === null) {
            if (gen === this.inventoryFetchGeneration) {
              this.inventoryLoading.set(false);
            }
            return EMPTY;
          }
          return of(mergeInventoryDtos(this.inventoryPayload(), dto));
        }),
        tap((merged) => {
          if (gen !== this.inventoryFetchGeneration) return;
          this.inventoryPayload.set(merged);
          this.inventoryHttpError.set(false);
          logShipCargoPayloadDiagnostic(merged);
          if (!merged.rateLimited) {
            this.inventoryCoordinator.markSuccessfulFetch();
          }
          if (merged.rateLimited) {
            this.capiRateLimit.record429(merged.retryAfterSeconds ?? null);
          } else {
            this.capiRateLimit.recordSuccess();
          }
        }),
        catchError((err: unknown) => {
          if (err instanceof HttpErrorResponse && err.status === 429) {
            console.debug('[FrontierVolume] HTTP 429 (GET inventaire, effet)');
            this.inventoryCoordinator.recordHttp429();
            const ra = parseRetryAfterSeconds(err);
            this.capiRateLimit.record429(ra);
            if (gen === this.inventoryFetchGeneration) {
              this.inventoryHttpError.set(false);
            }
            return of(null);
          }
          if (gen !== this.inventoryFetchGeneration) {
            return of(null);
          }
          this.inventoryHttpError.set(true);
          return of(null);
        }),
        finalize(() => {
          if (gen === this.inventoryFetchGeneration) {
            this.inventoryLoading.set(false);
          }
        }),
      )
      .subscribe();
  }

  private refreshChantierPipeline(id: number, stationLabel: string, mode: 'manual' | 'realSync'): Observable<void> {
    if (mode === 'realSync' && !this.chantierMarketCooldown.canPostRefreshOne(id)) {
      const sec = this.chantierMarketCooldown.secondsUntilRefreshAllowed(id);
      console.debug(
        `[Logistics] skipping refresh-one due to cooldown (${sec}s) chantierId=${id} — marketId bloquant récent`,
      );
      return of(void 0);
    }

    type MarketNoCapi = { readonly _tag: 'marketNoCapi'; message: string; code: string };

    return this.declaredApi.refreshOne(id).pipe(
      catchError((err: unknown) => {
        if (err instanceof HttpErrorResponse && err.status === 404) {
          return this.reloadChantierListsFromServer().pipe(
            tap(({ mine, others }) => {
              this.chantiersStore.replaceMineAndOthers(
                mine.map(mapDeclaredListItemApiToSite),
                others.map(mapDeclaredListItemApiToSite),
              );
              this.applyChantierGoneState(mode, id, stationLabel);
            }),
            switchMap(() => EMPTY),
          );
        }
        if (err instanceof HttpErrorResponse && err.status === 400) {
          const body = err.error as { code?: string; message?: string; requiresRedock?: boolean } | undefined;
          if (body?.code === CHANTIER_REFRESH_ERROR_MARKET_NO_CAPI_BLOCK) {
            const mark: MarketNoCapi = {
              _tag: 'marketNoCapi',
              message: typeof body.message === 'string' ? body.message : '',
              code: body.code ?? CHANTIER_REFRESH_ERROR_MARKET_NO_CAPI_BLOCK,
            };
            return of(mark);
          }
        }
        return throwError(() => err);
      }),
      mergeMap((dtoOrMark) => {
        if (
          typeof dtoOrMark === 'object' &&
          dtoOrMark != null &&
          '_tag' in dtoOrMark &&
          (dtoOrMark as MarketNoCapi)._tag === 'marketNoCapi'
        ) {
          const m = dtoOrMark as MarketNoCapi;
          this.applyMarketNoChantierCapiBlock(id, stationLabel, m.message, m.code, mode);
          return of(void 0);
        }
        const dto = dtoOrMark as DeclaredChantierListItemApi;
        return this.reloadChantierListsFromServer().pipe(
          map(({ mine, others }) => {
            const prev = this.ui.selectedSiteId();
            this.chantiersStore.replaceMineAndOthers(
              mine.map(mapDeclaredListItemApiToSite),
              others.map(mapDeclaredListItemApiToSite),
            );
            if (prev && !dto.active) {
              this.ui.selectedSiteId.set(null);
              const ctx = this.chantierCtx(id, stationLabel);
              if (mode === 'manual') {
                this.syncLog.addLog(`[Chantiers] ${ctx} · terminé — passé inactive`);
              } else {
                this.syncLog.addLog(`[Chantiers] ${ctx} · Real sync — terminé`);
              }
            } else if (dto.active) {
              const ctx = this.chantierCtx(id, stationLabel);
              if (mode === 'manual') {
                this.syncLog.addLog(`[Chantiers] ${ctx} · refresh manuel OK — remaining updated`);
              } else {
                this.syncLog.addLog(`[Chantiers] ${ctx} · Real sync OK`);
              }
            }
            return dto;
          }),
          tap(() => {
            this.chantierRefreshVerifiedThisCycle = true;
            if (mode === 'manual') {
              this.ui.touchChantierDataRefreshSuccess();
            }
            this.ui.clearChantierMarketIdCapiIssue();
            this.chantierMarketCooldown.clear(id);
            this.ui.setChantierServerListStaleDueToRateLimit(false);
          }),
          map(() => void 0),
        );
      }),
      take(1),
    );
  }

  private applyMarketNoChantierCapiBlock(
    chantierId: number,
    stationLabel: string,
    message: string,
    code: string,
    mode: 'manual' | 'realSync',
  ): void {
    const site = this.ui.selectedSite();
    this.ui.setChantierMarketIdCapiIssue({
      code,
      message,
      requiresRedock: true,
    });
    this.chantierMarketCooldown.recordMarketNoCapiBlock(chantierId);
    console.debug('[Logistics] MARKET_NO_CHANTIER_CAPI_BLOCK', {
      chantierId,
      siteName: stationLabel,
      marketId: site?.marketId ?? '—',
      endpoint: 'GET /market?marketId=',
      code,
      sourceMarketId: 'DeclaredChantier.MarketId (SQL, upsert dock / déclaration)',
      rowUpdatedAtUtc: 'voir sync serveur — UpdatedAtUtc chantier',
    });
    const ctx = this.chantierCtx(chantierId, stationLabel);
    this.syncLog.addLog(
      `[Chantiers] ${ctx} · Besoins chantier non vérifiables (marketId sans bloc CAPI). Redock pour réenregistrer.`,
    );
    if (mode === 'manual') {
      this.syncLog.addLog(`[Chantiers] ${ctx} · ${message}`);
    }
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
          this.syncLog.addLog(`[Chantiers] ${this.chantierCtx(id, stationLabel)} · supprimé`);
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
          this.syncLog.addLog(`[Chantiers] ${this.chantierCtx(id, stationLabel)} · suppression échec — ${msg}`);
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
        switchMap(() => this.inventoryCoordinator.getInventory$('manual').pipe(take(1))),
        tap((dto) => {
          if (!dto) return;
          const gen = ++this.inventoryFetchGeneration;
          const merged = mergeInventoryDtos(this.inventoryPayload(), dto);
          if (gen !== this.inventoryFetchGeneration) return;
          this.inventoryPayload.set(merged);
          this.inventoryHttpError.set(false);
          logShipCargoPayloadDiagnostic(merged);
          if (!merged.rateLimited) {
            this.inventoryCoordinator.markSuccessfulFetch();
          }
          if (merged.rateLimited) {
            this.capiRateLimit.record429(merged.retryAfterSeconds ?? null);
          } else {
            this.capiRateLimit.recordSuccess();
          }
        }),
        catchError((err: unknown) => {
          let msg = 'rafraîchissement impossible';
          if (err instanceof HttpErrorResponse) {
            const e = err.error as { message?: string } | undefined;
            if (typeof e?.message === 'string') msg = e.message;
            else if (err.status === 404) msg = 'chantier introuvable ou terminé';
            if (err.status === 429) {
              this.inventoryCoordinator.recordHttp429();
              const ra = parseRetryAfterSeconds(err);
              this.capiRateLimit.record429(ra);
            }
          }
          this.syncLog.addLog(`[Chantiers] ${this.chantierCtx(id, stationLabel)} · refresh manuel échec — ${msg}`);
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
