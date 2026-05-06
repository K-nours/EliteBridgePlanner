import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TruncateTooltipDirective } from '../../../shared/directives/truncate-tooltip.directive';
import { HttpErrorResponse } from '@angular/common/http';
import { catchError, forkJoin, map, of, switchMap, take } from 'rxjs';
import { FrontierChantiersDeclareEvaluateService } from '../../../core/services/frontier-chantiers-declare-evaluate.service';
import { DeclaredChantiersApiService } from '../../../core/services/declared-chantiers-api.service';
import type { DeclaredChantierListItemApi } from '../../../core/models/declared-chantiers-api.model';
import { SyncLogService } from '../../../core/services/sync-log.service';
import { ActiveChantiersStore, type ActiveChantierSite } from '../../../core/state/active-chantiers.store';
import { ChantierLogisticsUiService } from '../../../core/services/chantier-logistics-ui.service';
import { mapDeclaredListItemApiToSite } from '../../../core/utils/declared-chantiers-site-map';
import {
  formatChantiersDeclareEvaluateHttpError,
  formatChantiersDeclareEvaluateSyncLog,
} from '../../../core/utils/chantiers-inspect-sync-log';

export interface GroupedChantiersBySystem {
  systemName: string;
  sites: ActiveChantierSite[];
  count: number;
}

/**
 * Chantiers en cours : évaluation Frontier + persistance SQL + liste alignée sur le panneau systèmes.
 */
@Component({
  selector: 'app-chantiers-debug-panel',
  standalone: true,
  imports: [CommonModule, TruncateTooltipDirective],
  templateUrl: './chantiers-debug-panel.component.html',
  styleUrl: './chantiers-debug-panel.component.scss',
})
export class ChantiersDebugPanelComponent implements OnInit {
  private readonly declareEvaluateApi = inject(FrontierChantiersDeclareEvaluateService);
  private readonly declaredApi = inject(DeclaredChantiersApiService);
  private readonly syncLog = inject(SyncLogService);
  protected readonly chantiers = inject(ActiveChantiersStore);
  protected readonly logisticsUi = inject(ChantierLogisticsUiService);

  protected readonly loading = signal(false);
  protected readonly hydrating = signal(true);
  protected readonly shortStatus = signal<string | null>(null);
  protected readonly statusIsError = signal(false);
  /** Rechargement liste après clic refresh sur une ligne système. */
  protected readonly refreshingSystem = signal<string | null>(null);

  protected readonly mineSectionExpanded = signal(true);
  protected readonly othersSectionExpanded = signal(false);

  private readonly collapsedSystems = signal<Record<string, boolean>>({});

  protected readonly groupedMine = computed(() =>
    groupActiveBySystem(this.chantiers.mine().filter((e) => e.active)),
  );

  protected readonly groupedOthers = computed(() =>
    groupActiveBySystem(this.chantiers.others().filter((e) => e.active)),
  );

  protected readonly mineSiteCount = computed(() =>
    this.chantiers.mine().filter((e) => e.active).length,
  );

  protected readonly othersSiteCount = computed(() =>
    this.chantiers.others().filter((e) => e.active).length,
  );

  ngOnInit(): void {
    this.loadChantiersFromBackend();
  }

  private loadChantiersFromBackend(): void {
    const prevMine = this.chantiers.mine();
    const prevOthers = this.chantiers.others();

    const safeList = (obs$: ReturnType<typeof this.declaredApi.listMine>, prev: typeof prevMine) =>
      obs$.pipe(
        catchError((err: unknown) => {
          if (err instanceof HttpErrorResponse && err.status === 401) {
            // Token Frontier expiré — on garde les données existantes sans écraser le store
            this.shortStatus.set('Reconnectez Frontier pour voir vos chantiers');
            this.statusIsError.set(true);
            return of(prev.map((s): DeclaredChantierListItemApi => ({
              id: Number(s.id),
              systemName: s.systemName,
              stationName: s.stationName,
              cmdrName: s.cmdrName ?? '',
              active: s.active,
              declaredAtUtc: s.declaredAt,
              updatedAtUtc: s.declaredAt,
              marketId: s.marketId ?? null,
              constructionResources: s.constructionResources ?? [],
              constructionResourcesTotal: s.constructionResourcesTotal ?? 0,
            })));
          }
          return of([] as DeclaredChantierListItemApi[]);
        }),
      );

    forkJoin({
      mine: safeList(this.declaredApi.listMine(), prevMine),
      others: safeList(this.declaredApi.listOthers(), prevOthers),
    })
      .pipe(take(1))
      .subscribe(({ mine, others }) => {
        this.hydrating.set(false);
        this.chantiers.replaceMineAndOthers(mine.map(mapDeclaredListItemApiToSite), others.map(mapDeclaredListItemApiToSite));
      });
  }

  /** Clé unique par section (m/o) + nom de système — évite collision mine / autres. */
  protected systemRowKey(section: 'm' | 'o', systemName: string): string {
    return `${section}:${systemName}`;
  }

  protected toggleMineSection(): void {
    this.mineSectionExpanded.update((v) => !v);
  }

  protected toggleOthersSection(): void {
    this.othersSectionExpanded.update((v) => !v);
  }

  protected isSystemExpanded(systemName: string): boolean {
    return !(this.collapsedSystems()[systemName] ?? false);
  }

  protected toggleSystem(systemName: string): void {
    this.collapsedSystems.update((m) => {
      const cur = m[systemName] ?? false;
      return { ...m, [systemName]: !cur };
    });
  }

  protected selectConstructionSite(site: ActiveChantierSite, event: Event): void {
    event.stopPropagation();
    this.logisticsUi.selectSite(site);
  }

  protected refreshSystemData(event: Event, systemKey: string): void {
    event.stopPropagation();
    if (this.refreshingSystem()) return;
    this.refreshingSystem.set(systemKey);
    const systemName = systemKey.includes(':') ? systemKey.slice(systemKey.indexOf(':') + 1) : systemKey;
    this.syncLog.addLog(`[Chantiers] refresh liste — POST …/refresh-all · système affiché=${systemName}`);
    this.declaredApi
      .refreshAll()
      .pipe(
        switchMap((res) =>
          forkJoin({
            mine: this.declaredApi.listMine().pipe(catchError(() => of([] as DeclaredChantierListItemApi[]))),
            others: this.declaredApi.listOthers().pipe(catchError(() => of([] as DeclaredChantierListItemApi[]))),
          }).pipe(
            map(({ mine, others }) => {
              this.chantiers.replaceMineAndOthers(mine.map(mapDeclaredListItemApiToSite), others.map(mapDeclaredListItemApiToSite));
              return { res, mine, others };
            }),
          ),
        ),
        take(1),
      )
      .subscribe({
        next: ({ res, mine, others }) => {
          this.refreshingSystem.set(null);
          let line = `[Chantiers] refresh manuel OK — ${res.updated} chantier(s) mis à jour`;
          if (res.deactivated > 0) line += ` — ${res.deactivated} terminé(s)`;
          if (res.skipped > 0) line += ` — ${res.skipped} ignoré(s)`;
          this.syncLog.addLog(line);
          if (res.note) this.syncLog.addLog(`[Chantiers] ${res.note}`);
        },
        error: (err: unknown) => {
          this.refreshingSystem.set(null);
          let msg = 'Rafraîchissement impossible.';
          if (err instanceof HttpErrorResponse) {
            const e = err.error as { message?: string } | undefined;
            if (typeof e?.message === 'string') msg = e.message;
            else if (err.status === 400 && typeof err.error === 'object' && err.error !== null && 'message' in err.error) {
              msg = String((err.error as { message: string }).message);
            }
          }
          this.syncLog.addLog(`[Chantiers] refresh échec — ${msg}`);
        },
      });
  }

  /** Clic sur l'icône + du header — même logique que l'ancien bouton texte. */
  protected onAddChantierClick(event: Event): void {
    event.stopPropagation();
    this.declareDockedChantier();
  }

  protected declareDockedChantier(): void {
    if (this.loading()) return;
    this.loading.set(true);
    this.shortStatus.set(null);
    this.statusIsError.set(false);
    const requestUrl = this.declareEvaluateApi.getEvaluateRequestUrl();

    this.declareEvaluateApi.evaluate().pipe(take(1)).subscribe({
      next: (res) => {
        const pushSync = (outcomeLine: string) => {
          this.syncLog.setChantiersInspectLog(formatChantiersDeclareEvaluateSyncLog(requestUrl, res, outcomeLine));
        };

        if (!res.ok || !res.canDeclareChantier) {
          this.loading.set(false);
          this.statusIsError.set(true);
          this.shortStatus.set(res.userMessage || "Impossible d'ajouter ce chantier.");
          pushSync(`Action: refus — ${res.error ?? '—'}`);
          return;
        }

        const ms = res.marketSummary;
        if (!ms || !res.systemName || !res.stationName) {
          this.loading.set(false);
          this.statusIsError.set(true);
          this.shortStatus.set('Données chantier incomplètes.');
          pushSync('Action: refus — résumé marché manquant');
          return;
        }

        const resources = ms.constructionResources.map((r) => ({
          name: r.name,
          required: Number(r.required),
          provided: Number(r.provided),
          remaining: Number(r.remaining),
        }));

        this.declaredApi
          .persist({
            systemName: res.systemName,
            stationName: res.stationName,
            marketId: res.marketId ?? null,
            commanderName: res.commanderName ?? null,
            constructionResources: resources,
            constructionResourcesTotal: ms.constructionResourcesCount,
          })
          .pipe(
            switchMap(() =>
              forkJoin({
                mine: this.declaredApi.listMine().pipe(catchError(() => of([] as DeclaredChantierListItemApi[]))),
                others: this.declaredApi.listOthers().pipe(catchError(() => of([] as DeclaredChantierListItemApi[]))),
              }),
            ),
            map(({ mine, others }) => ({ mine, others, res, pushSync })),
            take(1),
          )
          .subscribe({
            next: ({ mine, others, res: r, pushSync: push }) => {
              this.loading.set(false);
              this.chantiers.replaceMineAndOthers(mine.map(mapDeclaredListItemApiToSite), others.map(mapDeclaredListItemApiToSite));
              this.statusIsError.set(false);
              this.shortStatus.set('Chantier enregistré.');
              push(
                `Action: chantier enregistré — ${r.systemName} / ${r.stationName} · marketId=${r.marketId ?? '—'}`,
              );
            },
            error: (err: unknown) => {
              this.loading.set(false);
              let msg = 'Enregistrement impossible.';
              if (err instanceof HttpErrorResponse) {
                if (err.status === 401) msg = 'Connexion Frontier requise pour enregistrer le chantier.';
                else if (err.error?.message) msg = String(err.error.message);
              }
              this.statusIsError.set(true);
              this.shortStatus.set(msg);
              pushSync(`Action: échec persistance — ${msg}`);
            },
          });
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.statusIsError.set(true);
        this.shortStatus.set('Erreur réseau — voir « État de la synchronisation ».');

        let status = 0;
        let message = 'Erreur réseau ou serveur';
        if (err instanceof HttpErrorResponse) {
          status = err.status;
          message = err.message || err.error?.message || String(err.status);
          if (err.status === 0) {
            message = 'Impossible de joindre le serveur (CORS, réseau ou backend arrêté)';
          }
        } else if (typeof err === 'object' && err !== null && 'message' in err) {
          message = String((err as { message?: string }).message);
        }

        this.syncLog.setChantiersInspectLog(formatChantiersDeclareEvaluateHttpError(requestUrl, status, message));
      },
    });
  }
}

function groupActiveBySystem(entries: ActiveChantierSite[]): GroupedChantiersBySystem[] {
  const map = new Map<string, ActiveChantierSite[]>();
  for (const e of entries) {
    const list = map.get(e.systemName) ?? [];
    list.push(e);
    map.set(e.systemName, list);
  }
  return Array.from(map.entries())
    .map(([systemName, sites]) => ({
      systemName,
      sites: [...sites].sort((a, b) => a.stationName.localeCompare(b.stationName, undefined, { sensitivity: 'base' })),
      count: sites.length,
    }))
    .sort((a, b) => a.systemName.localeCompare(b.systemName, undefined, { sensitivity: 'base' }));
}
