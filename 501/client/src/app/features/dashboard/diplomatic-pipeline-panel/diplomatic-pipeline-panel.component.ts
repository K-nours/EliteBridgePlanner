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

interface CachedFactionInfo {
  data: InaraFactionInfoDto;
  cachedAt: number; // timestamp ms
}

const FACTION_CACHE_KEY = 'eb-faction-info-v1';
const FACTION_CACHE_TTL_MS = 7 * 24 * 60 * 60 * 1000; // 7 jours

function loadFactionCache(): Map<string, CachedFactionInfo> {
  try {
    const raw = localStorage.getItem(FACTION_CACHE_KEY);
    if (!raw) return new Map();
    const parsed = JSON.parse(raw) as Record<string, CachedFactionInfo>;
    const now = Date.now();
    const valid = new Map<string, CachedFactionInfo>();
    for (const [k, v] of Object.entries(parsed)) {
      if (v?.cachedAt && now - v.cachedAt < FACTION_CACHE_TTL_MS) {
        valid.set(k, v);
      }
    }
    return valid;
  } catch {
    return new Map();
  }
}

function saveFactionCache(cache: Map<string, CachedFactionInfo>): void {
  try {
    const obj: Record<string, CachedFactionInfo> = {};
    for (const [k, v] of cache.entries()) obj[k] = v;
    localStorage.setItem(FACTION_CACHE_KEY, JSON.stringify(obj));
  } catch { /* quota ou incognito */ }
}

@Component({
  selector: 'app-diplomatic-pipeline-panel',
  standalone: true,
  templateUrl: './diplomatic-pipeline-panel.component.html',
  styleUrl: './diplomatic-pipeline-panel.component.scss',
})
export class DiplomaticPipelinePanelComponent implements OnInit, OnDestroy {
  private readonly api = inject(GuildSystemsApiService);
  private readonly guildSystemsSync = inject(GuildSystemsSyncService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly factionCache = loadFactionCache();

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
        // Persiste dans le cache localStorage
        this.factionCache.set(factionName, { data, cachedAt: Date.now() });
        saveFactionCache(this.factionCache);
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
    this.api.getDiplomaticPipeline().subscribe({
      next: (dto: DiplomaticPipelineDto) => {
        this.entries.set(dto.entries ?? []);
        this.edsmAvailable.set(dto.edsmAvailable);
        this.errorMessage.set(null);
        this.state.set('loaded');
        // Restaure les données cachées pour les factions présentes dans le pipeline
        const m = new Map(this.factionSyncStates());
        let changed = false;
        for (const entry of dto.entries ?? []) {
          const faction = entry.dominantFaction ?? '—';
          if (!m.has(faction) || m.get(faction)!.status === 'idle') {
            const cached = this.factionCache.get(faction);
            if (cached) {
              m.set(faction, { status: 'loaded', data: cached.data });
              changed = true;
            }
          }
        }
        if (changed) this.factionSyncStates.set(m);
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
