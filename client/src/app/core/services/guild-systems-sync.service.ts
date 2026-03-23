/**
 * Feature archived – no reliable external data source for Faction → Systems → Influence %.
 * Conserver pour R&D futur. Voir docs/GUILD-SYSTEMS.md § Raison de clôture.
 */
import { Injectable, inject, signal } from '@angular/core';
import { GuildSystemsApiService } from './guild-systems-api.service';
import { SyncLogService } from './sync-log.service';
import type { GuildSystemsResponseDto, SystemsFilterValue } from '../models/guild-systems.model';
import { timeout } from 'rxjs';

export type GuildSystemsPanelState = 'not-synced' | 'loading' | 'failed' | 'cached';

const SYNC_TIMEOUT_MS = 15_000;
const MAX_ATTEMPTS = 3; // 1 initial + 2 retries

/** Orchestre la sync BGS et l'état du panneau Guild Systems. Déclenché par le bouton central du dashboard. */
@Injectable({ providedIn: 'root' })
export class GuildSystemsSyncService {
  private readonly api = inject(GuildSystemsApiService);
  private readonly syncLog = inject(SyncLogService);

  readonly panelState = signal<GuildSystemsPanelState>('loading');
  readonly systems = signal<GuildSystemsResponseDto>({
    origin: [],
    headquarter: [],
    surveillance: [],
    conflicts: [],
    critical: [],
    low: [],
    healthy: [],
    others: [],
    dataSource: 'seed',
    influenceThresholds: { critical: 10, low: 30, high: 60 },
    tacticalThresholds: { critical: 5, low: 15, high: 60 },
  });
  readonly lastError = signal<string | null>(null);

  /** Filtre actif pour la colonne systèmes et la carte. */
  readonly systemsFilter = signal<SystemsFilterValue>('all');

  /** Dernière tentative de sync (succès ou échec) */
  readonly lastAttemptAt = signal<Date | null>(null);
  /** Dernière sync réussie */
  readonly lastSuccessfulSyncAt = signal<Date | null>(null);
  /** Nombre de systèmes mis à jour lors du dernier succès */
  readonly lastSystemsUpdated = signal<number>(0);
  /** Message d'erreur du dernier échec */
  readonly lastErrorMessage = signal<string | null>(null);

  loadSystems(): void {
    this.panelState.set('loading');
    this.lastError.set(null);
    this.api.getSystems().subscribe({
      next: (data: GuildSystemsResponseDto) => {
        this.lastError.set(null);
        const ds = data?.dataSource === 'cached' ? 'cached' : 'seed';
        this.systems.set({
          origin: data?.origin ?? [],
          headquarter: data?.headquarter ?? [],
          surveillance: data?.surveillance ?? [],
          conflicts: data?.conflicts ?? [],
          critical: data?.critical ?? [],
          low: data?.low ?? [],
          healthy: data?.healthy ?? [],
          others: data?.others ?? [],
          dataSource: ds,
          influenceThresholds: data?.influenceThresholds ?? { critical: 10, low: 30, high: 60 },
          tacticalThresholds: data?.tacticalThresholds ?? { critical: 5, low: 15, high: 60 },
        });
        this.panelState.set(ds === 'cached' ? 'cached' : 'not-synced');
      },
      error: (err) => {
        const msg = err?.error?.error ?? err?.error?.message ?? err?.message ?? 'Erreur inconnue';
        this.lastError.set(msg);
        this.panelState.set('failed');
        this.syncLog.addLog('Guild Systems: erreur chargement — ' + msg);
      },
    });
  }

  /** Lance la sync BGS. Appelé par le bouton central du dashboard. */
  sync(): void {
    if (this.panelState() === 'loading') return;

    this.panelState.set('loading');
    this.lastError.set(null);
    this.lastErrorMessage.set(null);
    this.lastAttemptAt.set(new Date());

    const startMs = performance.now();
    this.syncLog.addLog('BGS sync démarrée (timeout ' + SYNC_TIMEOUT_MS / 1000 + 's, max ' + MAX_ATTEMPTS + ' tentative(s))');

    const tryAttempt = (attempt: number) => {
      this.syncLog.addLog(`BGS sync tentative ${attempt}/${MAX_ATTEMPTS}...`);

      this.api.syncBgs().pipe(
        timeout(SYNC_TIMEOUT_MS),
      ).subscribe({
        next: (res) => {
          const durationMs = Math.round(performance.now() - startMs);
          this.lastSuccessfulSyncAt.set(new Date());
          this.lastSystemsUpdated.set(res.updated);
          this.lastErrorMessage.set(null);
          this.syncLog.addLog(`BGS sync succès: ${res.updated} système(s) mis à jour (${durationMs}ms)`);
          this.loadSystems();
        },
        error: (err) => {
          const msg = err?.error?.error ?? err?.error?.message ?? err?.message ?? (err?.name === 'TimeoutError' ? 'Délai dépassé (' + SYNC_TIMEOUT_MS / 1000 + 's)' : 'Erreur inconnue');
          const durationMs = Math.round(performance.now() - startMs);

          if (attempt < MAX_ATTEMPTS) {
            this.syncLog.addLog(`Tentative ${attempt} échouée: ${msg} — nouvel essai...`);
            tryAttempt(attempt + 1);
          } else {
            this.lastError.set(msg);
            this.lastErrorMessage.set(msg);
            this.panelState.set('failed');
            this.syncLog.addLog(`BGS sync échec après ${MAX_ATTEMPTS} tentative(s): ${msg} (durée totale ${durationMs}ms)`);
          }
        },
      });
    };

    tryAttempt(1);
  }
}
