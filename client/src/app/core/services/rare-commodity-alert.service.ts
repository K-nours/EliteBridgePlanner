import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';

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
  date: string; // YYYY-MM-DD
  state: AlertState;
}

/**
 * Vérifie une fois par jour si une rare commodity sur Inara dépasse 350t.
 * Cache le résultat dans localStorage — pas d'appel redondant dans la journée.
 */
@Injectable({ providedIn: 'root' })
export class RareCommodityAlertService {
  private readonly http     = inject(HttpClient);
  private readonly CACHE_KEY = 'guild_rare_commodity_alert_v1';
  private readonly BASE_URL  = 'https://inara.cz/elite/commodities-rare/';

  private readonly _state      = signal<AlertState | null>(null);
  private readonly _loading    = signal(false);
  private readonly _error      = signal<string | null>(null);
  private readonly _lastCheck  = signal<string | null>(null); // YYYY-MM-DD

  readonly loading = this._loading.asReadonly();
  readonly error   = this._error.asReadonly();

  /** True si au moins une commodité dépasse le seuil. */
  readonly hasAlert = computed(() => this._state()?.hasAlert ?? false);

  /** True une fois que la vérification du jour a abouti (avec ou sans alerte). */
  readonly isReady = computed(() => this._state() !== null);

  readonly alertCommodities = computed(() => this._state()?.commodities ?? []);

  /** URL Inara cible : page commodité si une seule alerte, sinon liste générale. */
  readonly alertUrl = computed((): string => {
    const list = this.alertCommodities();
    return list.length === 1 ? list[0].url : this.BASE_URL;
  });

  /** Tooltip détaillé pour le bouton warning. */
  readonly alertTooltip = computed((): string => {
    const lines = this.alertCommodities().map(c => `${c.name} (${c.supplyT} t)`);
    const date  = this._lastCheck();
    if (date) lines.push(`Vérifié le ${this.formatDate(date)}`);
    return lines.join('\n');
  });

  /** Tooltip pour le bouton check (état OK). */
  readonly okTooltip = computed((): string => {
    const date = this._lastCheck();
    return date
      ? `Rare commodities OK\nVérifié le ${this.formatDate(date)}`
      : 'Rare commodities — aucune alerte';
  });

  // ── API publique ──────────────────────────────────────────────────────────

  checkOncePerDay(): void {
    const today  = this.todayStr();
    const cached = this.readCache();
    if (cached?.date === today) {
      this._state.set(cached.state);
      this._lastCheck.set(cached.date);
      return;
    }
    this.fetch(today);
  }

  // ── Privé ─────────────────────────────────────────────────────────────────

  private fetch(today: string): void {
    this._loading.set(true);
    this._error.set(null);
    this.http.get<AlertState>('/api/inara/rare-commodities').subscribe({
      next: (state) => {
        this._state.set(state);
        this._lastCheck.set(today);
        this.writeCache({ date: today, state });
        this._loading.set(false);
      },
      error: () => {
        this._error.set('Inara unreachable');
        this._loading.set(false);
      },
    });
  }

  private todayStr(): string {
    return new Date().toISOString().split('T')[0];
  }

  private readCache(): CachedAlert | null {
    try {
      const raw = localStorage.getItem(this.CACHE_KEY);
      return raw ? (JSON.parse(raw) as CachedAlert) : null;
    } catch { return null; }
  }

  private writeCache(data: CachedAlert): void {
    try { localStorage.setItem(this.CACHE_KEY, JSON.stringify(data)); }
    catch { /* quota — on ignore */ }
  }

  /** Formate "2026-04-28" → "28/04/2026" */
  private formatDate(iso: string): string {
    const [y, m, d] = iso.split('-');
    return `${d}/${m}/${y}`;
  }
}
