import { Component, inject, signal, computed, DestroyRef, OnInit, OnDestroy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { toObservable } from '@angular/core/rxjs-interop';
import { debounceTime, filter, skip } from 'rxjs';
import { GuildSystemsApiService } from '../../../core/services/guild-systems-api.service';
import { GuildSystemsSyncService } from '../../../core/services/guild-systems-sync.service';
import type { DiplomaticPipelineEntryDto, DiplomaticPipelineDto, InaraFactionInfoDto } from '../../../core/models/diplomatic-pipeline.model';

type PanelState = 'idle' | 'loading' | 'loaded' | 'error';
type FactionSyncStatus = 'idle' | 'loading' | 'loaded' | 'error';

interface FactionGroup {
  factionName: string;
  systems: DiplomaticPipelineEntryDto[];
}

interface FactionSyncState {
  status: FactionSyncStatus;
  data?: InaraFactionInfoDto;
  error?: string;
}

@Component({
  selector: 'app-diplomatic-pipeline-panel',
  standalone: true,
  template: `
    <div class="dp-header">
      <h3>Menaces diplomatiques</h3>
    </div>

    @if (state() === 'error') {
      <div class="dp-empty dp-empty--error">
        <span class="dp-empty-icon">⚠</span>
        <span>{{ errorMessage() }}</span>
      </div>
    } @else if (factionGroups().length > 0) {
      @if (!edsmAvailable()) {
        <div class="dp-edsm-warning">EDSM indisponible — factions non chargées</div>
      }
      <div class="dp-groups">
        @for (group of factionGroups(); track group.factionName) {
          <div class="category"
            [class.category--collapsed]="isCollapsed(group.factionName)">
            <button
              type="button"
              class="category-header category-header--clickable"
              [class.category-header--expanded]="!isCollapsed(group.factionName)"
              (click)="toggleGroup(group.factionName)"
              [attr.aria-expanded]="!isCollapsed(group.factionName)"
            >
              <span class="category-label">{{ group.factionName }}</span>
              <span class="category-count">({{ group.systems.length }})</span>
              <svg class="category-chevron" xmlns="http://www.w3.org/2000/svg" width="10" height="10"
                viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="6 9 12 15 18 9"/>
              </svg>
            </button>
            <div class="category-expand"
              [class.category-expand--open]="!isCollapsed(group.factionName)"
              [attr.aria-hidden]="isCollapsed(group.factionName)">
              <div class="category-expand-inner">
                <div class="category-content">

                  <!-- Bloc info faction -->
                  @if (getFactionSync(group.factionName).status === 'idle') {
                    <p class="faction-info">
                      Aucune information sur cette faction pour l'instant.
                      <button
                        type="button"
                        class="btn-inara-sync"
                        (click)="syncFactionInfo(group.factionName, group.systems[0]?.systemName); $event.stopPropagation()"
                        title="Récupérer les infos depuis Inara"
                      >Synchro Inara</button>
                    </p>
                  } @else if (getFactionSync(group.factionName).status === 'loading') {
                    <div class="faction-sync-loading">
                      <span class="dp-spinner"></span>
                      <span>Chargement depuis Inara…</span>
                    </div>
                  } @else if (getFactionSync(group.factionName).status === 'error') {
                    <p class="faction-info faction-info--error">
                      <span>⚠ {{ getFactionSync(group.factionName).error }}</span>
                      <button
                        type="button"
                        class="btn-inara-sync"
                        (click)="syncFactionInfo(group.factionName, group.systems[0]?.systemName); $event.stopPropagation()"
                      >Réessayer</button>
                    </p>
                  } @else if (getFactionSync(group.factionName).status === 'loaded') {
                    @let info = getFactionSync(group.factionName).data;
                    @if (info) {
                      <div class="faction-info-block">
                        <div class="faction-info-grid">
                          @if (info.allegiance) {
                            <span class="fi-label">Allégeance</span>
                            <span class="fi-value">{{ info.allegiance }}</span>
                          }
                          @if (info.government) {
                            <span class="fi-label">Gouvernement</span>
                            <span class="fi-value">{{ info.government }}</span>
                          }
                          @if (info.origin) {
                            <span class="fi-label">Origine</span>
                            <span class="fi-value">{{ info.origin }}</span>
                          }
                          @if (info.isPlayerFaction !== null && info.isPlayerFaction !== undefined) {
                            <span class="fi-label">Faction joueur</span>
                            <span class="fi-value" [class.fi-value--yes]="info.isPlayerFaction">{{ info.isPlayerFaction ? 'Oui' : 'Non' }}</span>
                          }
                          @if (info.squadronName) {
                            <span class="fi-label">Escadron</span>
                            <span class="fi-value">
                              @if (info.squadronInaraUrl) {
                                <a [href]="info.squadronInaraUrl" target="_blank" rel="noopener noreferrer" class="fi-link">{{ info.squadronName }}</a>
                              } @else {
                                {{ info.squadronName }}
                              }
                            </span>
                          }
                          @if (info.squadronLanguage) {
                            <span class="fi-label fi-indent">↳ Langue</span>
                            <span class="fi-value">{{ info.squadronLanguage }}</span>
                          }
                          @if (info.squadronTimezone) {
                            <span class="fi-label fi-indent">↳ Fuseau</span>
                            <span class="fi-value">{{ info.squadronTimezone }}</span>
                          }
                          @if (info.squadronMembersCount !== null && info.squadronMembersCount !== undefined) {
                            <span class="fi-label fi-indent">↳ Membres</span>
                            <span class="fi-value">{{ info.squadronMembersCount }}</span>
                          }
                        </div>
                        @if (info.factionInaraUrl) {
                          <a [href]="info.factionInaraUrl" target="_blank" rel="noopener noreferrer" class="fi-inara-link">
                            Voir sur Inara ↗
                          </a>
                        }
                      </div>
                    }
                  }

                  @for (entry of group.systems; track entry.systemName) {
                    <div class="system-row">
                      <a
                        class="system-name"
                        [href]="inaraSystemUrl(entry.systemName)"
                        target="_blank"
                        rel="noopener noreferrer"
                        (click)="$event.stopPropagation()"
                      >{{ entry.systemName }}</a>
                      <span class="influence">{{ formatInfluence(entry.guildInfluencePercent) }}%</span>
                    </div>
                  }
                </div>
              </div>
            </div>
          </div>
        }
      </div>
    } @else if (state() === 'loading') {
      <div class="dp-loading">
        <span class="dp-spinner"></span>
        <span class="dp-loading-label">Chargement...</span>
      </div>
    } @else if (state() === 'loaded') {
      <div class="dp-empty dp-empty--ok">
        <span class="dp-empty-icon dp-empty-icon--ok">✓</span>
        <span>Aucun système critique</span>
      </div>
    }
  `,
  styles: [`
    :host {
      display: contents;
    }

    /* Header */
    .dp-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
      flex-shrink: 0;
    }

    h3 {
      margin: 0;
      font-family: 'Orbitron', sans-serif;
      font-size: 0.75rem;
      font-weight: 600;
      color: #00eaff;
      text-transform: uppercase;
      letter-spacing: 0.1em;
      text-shadow:
        0 0 4px rgba(0, 234, 255, 0.4),
        0 0 10px rgba(0, 234, 255, 0.22);
    }

    /* États vides / erreur / chargement */
    .dp-loading {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-top: 0.75rem;
      color: rgba(0, 212, 255, 0.7);
      font-size: 0.75rem;
      font-family: 'Orbitron', sans-serif;
    }
    .dp-loading-label { opacity: 0.7; }

    .dp-spinner {
      display: inline-block;
      width: 10px;
      height: 10px;
      border: 2px solid rgba(0, 212, 255, 0.25);
      border-top-color: #00d4ff;
      border-radius: 50%;
      animation: dp-spin 0.8s linear infinite;
      flex-shrink: 0;
    }
    @keyframes dp-spin { to { transform: rotate(360deg); } }

    .dp-empty {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-top: 0.75rem;
      font-size: 0.72rem;
      font-family: 'Orbitron', sans-serif;
      color: rgba(255, 255, 255, 0.45);
    }
    .dp-empty--error { color: rgba(248, 113, 113, 0.85); }
    .dp-empty-icon { font-size: 0.9rem; line-height: 1; }
    .dp-empty-icon--ok { color: rgba(110, 231, 183, 0.8); }

    .dp-edsm-warning {
      margin-top: 0.4rem;
      font-size: 0.65rem;
      color: rgba(251, 191, 36, 0.75);
      font-family: 'Orbitron', sans-serif;
      flex-shrink: 0;
    }

    .dp-groups {
      display: flex;
      flex-direction: column;
      margin-top: 1rem;
      margin-left: -8px;
      overflow-y: auto;
      overflow-x: hidden;
      flex: 1;
      min-height: 0;
    }

    /* === Même design que guild-systems-panel === */

    .category {
      margin-bottom: 1rem;
    }
    .category:last-child { margin-bottom: 0; }
    .category--collapsed { margin-bottom: 0.25rem; }

    .category-header {
      display: flex;
      align-items: center;
      gap: 0.35rem;
      width: 100%;
      padding: 0.2rem 8px;
      margin-bottom: 0.5rem;
      background: none;
      border: none;
      font: inherit;
      color: inherit;
      text-align: left;
      cursor: default;
    }
    .category-header--clickable {
      cursor: pointer;
      border-radius: 4px;
      transition: background 0.15s;
    }
    .category-header--clickable:hover {
      background: rgba(0, 212, 255, 0.08);
    }
    .category-header--clickable:focus-visible {
      outline: 1px solid rgba(0, 212, 255, 0.5);
      outline-offset: 2px;
    }
    .category-header--clickable .category-chevron {
      margin-left: auto;
      flex-shrink: 0;
    }

    .category-chevron {
      flex-shrink: 0;
      color: rgba(255, 255, 255, 0.92);
      opacity: 0.95;
      transition: transform 0.35s cubic-bezier(0.4, 0, 0.2, 1);
    }
    .category-header--expanded .category-chevron {
      transform: rotate(0deg);
    }
    .category-header:not(.category-header--expanded) .category-chevron {
      transform: rotate(-90deg);
    }

    .category-label {
      font-family: 'Orbitron', sans-serif;
      font-size: 0.65rem;
      font-weight: 600;
      letter-spacing: 0.05em;
      color: #ff8c00;
    }

    .category-count {
      font-family: 'Exo 2', sans-serif;
      font-size: 0.65rem;
      font-weight: 500;
      color: rgba(255, 255, 255, 0.5);
    }

    /* Animation dépli/repli */
    .category-expand {
      display: grid;
      grid-template-rows: 0fr;
      interpolate-size: allow-keywords;
      transition: grid-template-rows 0.38s cubic-bezier(0.4, 0, 0.2, 1);
    }
    .category-expand--open {
      grid-template-rows: 1fr;
    }
    .category-expand-inner {
      overflow: hidden;
      min-height: 0;
    }

    /* Info faction — ligne de base */
    .faction-info {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      flex-wrap: wrap;
      margin: 0 0 0.5rem 8px;
      font-family: 'Exo 2', sans-serif;
      font-size: 0.65rem;
      font-style: italic;
      color: rgba(255, 255, 255, 0.35);
    }
    .faction-info--error {
      color: rgba(248, 113, 113, 0.75);
      font-style: normal;
    }

    /* Lien "Synchro Inara" */
    .btn-inara-sync {
      display: inline-flex;
      align-items: center;
      padding: 0.1rem 0.45rem;
      background: rgba(255, 140, 0, 0.12);
      border: 1px solid rgba(255, 140, 0, 0.35);
      border-radius: 3px;
      font-family: 'Orbitron', sans-serif;
      font-size: 0.55rem;
      font-style: normal;
      font-weight: 600;
      letter-spacing: 0.05em;
      color: #ff8c00;
      cursor: pointer;
      transition: background 0.15s, border-color 0.15s;
      flex-shrink: 0;
    }
    .btn-inara-sync:hover {
      background: rgba(255, 140, 0, 0.22);
      border-color: rgba(255, 140, 0, 0.6);
    }

    /* État chargement sync Inara */
    .faction-sync-loading {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      margin: 0 0 0.5rem 8px;
      font-family: 'Exo 2', sans-serif;
      font-size: 0.65rem;
      color: rgba(255, 140, 0, 0.6);
    }

    /* Bloc infos faction chargé */
    .faction-info-block {
      margin: 0 0 0.6rem 8px;
    }

    .faction-info-grid {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: 0.15rem 0.6rem;
      margin-bottom: 0.4rem;
    }

    .fi-label {
      font-family: 'Orbitron', sans-serif;
      font-size: 0.58rem;
      font-weight: 600;
      letter-spacing: 0.04em;
      color: rgba(255, 255, 255, 0.4);
      white-space: nowrap;
      align-self: center;
    }
    .fi-indent {
      padding-left: 0.5rem;
      color: rgba(255, 255, 255, 0.28);
      font-size: 0.55rem;
    }

    .fi-value {
      font-family: 'Exo 2', sans-serif;
      font-size: 0.68rem;
      color: rgba(255, 255, 255, 0.75);
      align-self: center;
    }
    .fi-value--yes {
      color: rgba(110, 231, 183, 0.85);
    }

    .fi-link {
      color: #00d4ff;
      text-decoration: none;
      transition: color 0.15s;
    }
    .fi-link:hover {
      color: #7fffff;
      text-decoration: underline;
    }

    .fi-inara-link {
      display: inline-block;
      margin-top: 0.25rem;
      font-family: 'Orbitron', sans-serif;
      font-size: 0.55rem;
      color: rgba(255, 140, 0, 0.6);
      text-decoration: none;
      transition: color 0.15s;
    }
    .fi-inara-link:hover {
      color: #ff8c00;
    }

    /* Lignes systèmes */
    .category-content {
      padding-bottom: 0.5rem;
    }
    .system-row {
      position: relative;
      z-index: 0;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
      padding: 0.3rem 8px;
      min-height: 1.75rem;
    }
    .system-row::before {
      content: '';
      position: absolute;
      inset: 0;
      border-radius: 6px;
      background: transparent;
      transition: background 0.15s;
      z-index: 0;
    }
    .system-row:hover::before {
      background: rgba(0, 212, 255, 0.06);
    }

    .system-name {
      position: relative;
      z-index: 1;
      font-family: 'Exo 2', sans-serif;
      font-size: 0.72rem;
      font-weight: 500;
      color: rgba(255, 255, 255, 0.9);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      min-width: 0;
      text-decoration: none;
      transition: color 0.15s;
    }
    .system-name:hover {
      color: #00d4ff;
      text-decoration: underline;
    }

    .influence {
      position: relative;
      z-index: 1;
      font-family: 'Exo 2', sans-serif;
      font-size: 0.72rem;
      font-weight: 600;
      flex-shrink: 0;
      color: #ff6b6b;
    }

    @media (prefers-reduced-motion: reduce) {
      .category-expand,
      .category-expand--open,
      .category-chevron,
      .dp-spinner { transition: none; animation: none; }
    }
  `],
})
export class DiplomaticPipelinePanelComponent implements OnInit, OnDestroy {
  private readonly api = inject(GuildSystemsApiService);
  private readonly guildSystemsSync = inject(GuildSystemsSyncService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly messageHandler = (event: MessageEvent): void => {
    if (!event.origin.includes('inara.cz')) return;
    const { type, factionName, data, error } = event.data ?? {};
    if (!factionName) return;

    if (type === 'inara-faction-info') {
      const m = new Map(this.factionSyncStates());
      if (data?.error) {
        m.set(factionName, { status: 'error', error: data.error });
      } else {
        m.set(factionName, { status: 'loaded', data });
      }
      this.factionSyncStates.set(m);
    } else if (type === 'inara-faction-info-error') {
      const m = new Map(this.factionSyncStates());
      m.set(factionName, { status: 'error', error: error ?? 'Erreur inconnue' });
      this.factionSyncStates.set(m);
    }
  };

  readonly state = signal<PanelState>('idle');
  readonly entries = signal<DiplomaticPipelineEntryDto[]>([]);
  readonly errorMessage = signal<string | null>(null);
  readonly edsmAvailable = signal<boolean>(true);

  private readonly collapsedGroups = signal<Set<string>>(new Set());
  private readonly factionSyncStates = signal<Map<string, FactionSyncState>>(new Map());

  /** Entrées groupées par faction, triées par influence min croissante */
  readonly factionGroups = computed<FactionGroup[]>(() => {
    const map = new Map<string, DiplomaticPipelineEntryDto[]>();
    for (const entry of this.entries()) {
      const key = entry.dominantFaction ?? '—';
      if (!map.has(key)) map.set(key, []);
      map.get(key)!.push(entry);
    }
    return Array.from(map.entries())
      .map(([factionName, systems]) => ({ factionName, systems }))
      .sort((a, b) => {
        const minA = Math.min(...a.systems.map(s => s.guildInfluencePercent));
        const minB = Math.min(...b.systems.map(s => s.guildInfluencePercent));
        return minA - minB;
      });
  });

  constructor() {
    toObservable(this.guildSystemsSync.systems).pipe(
      skip(1),
      filter(s =>
        s.dataSource === 'cached' ||
        s.critical.length > 0 ||
        s.low.length > 0 ||
        s.healthy.length > 0 ||
        s.others.length > 0
      ),
      debounceTime(600),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(() => this.load());
  }

  ngOnInit(): void {
    window.addEventListener('message', this.messageHandler);
    this.load();
  }

  ngOnDestroy(): void {
    window.removeEventListener('message', this.messageHandler);
  }

  load(): void {
    if (this.state() === 'loading') return;
    this.state.set('loading');
    // Reset des états sync quand on recharge le pipeline
    this.factionSyncStates.set(new Map());
    this.api.getDiplomaticPipeline().subscribe({
      next: (dto: DiplomaticPipelineDto) => {
        this.entries.set(dto.entries ?? []);
        this.edsmAvailable.set(dto.edsmAvailable);
        this.errorMessage.set(null);
        this.state.set('loaded');
      },
      error: (err: unknown) => {
        const msg = (err as { error?: { error?: string; message?: string }; message?: string })?.error?.error
          ?? (err as { error?: { message?: string } })?.error?.message
          ?? (err as { message?: string })?.message
          ?? 'Erreur inconnue';
        this.errorMessage.set(msg);
        this.state.set('error');
      },
    });
  }

  /** Lance la récupération des infos Inara via Tampermonkey (window.open + postMessage). */
  protected syncFactionInfo(factionName: string, systemName: string | undefined): void {
    if (!systemName) return;

    const m = new Map(this.factionSyncStates());
    m.set(factionName, { status: 'loading' });
    this.factionSyncStates.set(m);

    // Timeout de 60s si Tampermonkey ne répond pas
    setTimeout(() => {
      const cur = this.factionSyncStates().get(factionName);
      if (cur?.status === 'loading') {
        const m2 = new Map(this.factionSyncStates());
        m2.set(factionName, { status: 'error', error: 'Pas de réponse (Tampermonkey installé et activé ?)' });
        this.factionSyncStates.set(m2);
      }
    }, 60_000);

    const hash = `fis=1&fn=${encodeURIComponent(factionName)}&oo=${encodeURIComponent(window.location.origin)}`;
    const url = `https://inara.cz/elite/starsystem/?search=${encodeURIComponent(systemName)}#${hash}`;
    window.open(url, '_blank');
  }

  protected getFactionSync(factionName: string): FactionSyncState {
    return this.factionSyncStates().get(factionName) ?? { status: 'idle' };
  }

  protected toggleGroup(factionName: string): void {
    const next = new Set(this.collapsedGroups());
    if (next.has(factionName)) next.delete(factionName);
    else next.add(factionName);
    this.collapsedGroups.set(next);
  }

  protected isCollapsed(factionName: string): boolean {
    return this.collapsedGroups().has(factionName);
  }

  protected formatInfluence(value: number): string {
    return value.toFixed(1);
  }

  protected inaraSystemUrl(systemName: string): string {
    return `https://inara.cz/elite/starsystem/?search=${encodeURIComponent(systemName)}`;
  }
}
