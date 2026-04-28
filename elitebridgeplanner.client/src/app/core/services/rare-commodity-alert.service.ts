import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';

// ── Types ────────────────────────────────────────────────────────────────────

export interface RareCommodity {
  name: string;
  supplyT: number;
  url: string;
}

interface AlertState {
  hasAlert: boolean;
  commodities: RareCommodity[];
}

interface CachedAlert {
  date: string;   // YYYY-MM-DD
  state: AlertState;
}

// ── Service ──────────────────────────────────────────────────────────────────

/**
 * Vérifie une fois par jour si des rare commodities sur Inara dépassent 350t.
 * Le résultat est mis en cache dans localStorage pour éviter un appel redondant.
 */
@Injectable({ providedIn: 'root' })
export class RareCommodityAlertService {
  private readonly http = inject(HttpClient);

  private readonly CACHE_KEY = 'ebp_rare_commodity_alert_v1';
  private readonly API_URL   = '/api/inara/rare-commodities';
  private readonly BASE_URL  = 'https://inara.cz/elite/commodities-rare/';

  // ── State signals ─────────────────────────────────────────────────────────

  private readonly _state   = signal<AlertState | null>(null);
  private readonly _loading = signal(false);
  private readonly _error   = signal<string | null>(null);

  readonly loading = this._loading.asReadonly();
  readonly error   = this._error.asReadonly();

  readonly hasAlert = computed(() => this._state()?.hasAlert ?? false);

  readonly alertCommodities = computed(() => this._state()?.commodities ?? []);

  /** URL Inara vers laquelle le warning button redirige. */
  readonly alertUrl = computed((): string => {
    const list = this.alertCommodities();
    // Si une seule commodité : lien direct vers sa page Inara
    if (list.length === 1) return list[0].url;
    // Plusieurs (ou aucune) : page liste globale
    return this.BASE_URL;
  });

  /** Tooltip du bouton warning — liste les commodités en alerte. */
  readonly alertTooltip = computed((): string => {
    const list = this.alertCommodities();
    if (list.length === 0) return '';
    return list.map(c => `${c.name} (${c.supplyT} t)`).join('\n');
  });

  // ── Public API ────────────────────────────────────────────────────────────

  /**
   * À appeler au chargement de l'app.
   * Si le cache du jour est dispo, l'utilise directement ;
   * sinon appelle le backend proxy.
   */
  checkOncePerDay(): void {
    const today  = this.todayStr();
    const cached = this.readCache();

    if (cached?.date === today) {
      this._state.set(cached.state);
      return;
    }

    this.fetchFromBackend(today);
  }

  // ── Private ───────────────────────────────────────────────────────────────

  private fetchFromBackend(today: string): void {
    this._loading.set(true);
    this._error.set(null);

    this.http.get<AlertState>(this.API_URL).subscribe({
      next: (state) => {
        this._state.set(state);
        this.writeCache({ date: today, state });
        this._loading.set(false);
      },
      error: () => {
        this._error.set('Inara unreachable');
        this._loading.set(false);
      }
    });
  }

  private todayStr(): string {
    return new Date().toISOString().split('T')[0];
  }

  private readCache(): CachedAlert | null {
    try {
      const raw = localStorage.getItem(this.CACHE_KEY);
      return raw ? (JSON.parse(raw) as CachedAlert) : null;
    } catch {
      return null;
    }
  }

  private writeCache(data: CachedAlert): void {
    try {
      localStorage.setItem(this.CACHE_KEY, JSON.stringify(data));
    } catch { /* quota dépassé — on ignore */ }
  }
}
