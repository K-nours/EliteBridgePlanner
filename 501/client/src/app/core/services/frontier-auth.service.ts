import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import type { FrontierAuthState, FrontierAuthStatus, FrontierMeResponse, FrontierProfileDto } from '../models/frontier-auth.model';

@Injectable({ providedIn: 'root' })
export class FrontierAuthService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/integrations/frontier';

  private readonly _state = signal<FrontierAuthState>('not-connected');
  private readonly _profile = signal<FrontierProfileDto | null>(null);
  private readonly _errorMessage = signal<string | null>(null);
  private readonly _loading = signal(false);

  readonly state = this._state.asReadonly();
  readonly profile = this._profile.asReadonly();
  readonly errorMessage = this._errorMessage.asReadonly();
  readonly loading = this._loading.asReadonly();

  readonly status = computed<FrontierAuthStatus>(() => ({
    state: this._state(),
    profile: this._profile(),
    errorMessage: this._errorMessage(),
  }));

  readonly isConnected = computed(() => this._state() === 'connected');
  readonly needsReconnect = computed(() =>
    ['expired', 'error'].includes(this._state())
  );
  readonly commanderName = computed(() => this._profile()?.commanderName ?? null);

  /** Lance le flux OAuth dans un popup (navigateur par défaut). */
  login(): void {
    const url = `${this.base}/start`;
    const popup = window.open(url, 'frontier-oauth', 'width=520,height=680,scrollbars=yes,resizable=yes');
    if (!popup) {
      window.location.href = url;
      return;
    }
    const origin = window.location.origin;
    const handler = (e: MessageEvent) => {
      if (e.origin !== origin || e.data?.type !== 'frontier-oauth-done') return;
      window.removeEventListener('message', handler);
      if (e.data.success) this.checkAndLoadProfile();
      else this.setError(e.data.message ?? 'Erreur');
    };
    window.addEventListener('message', handler);
    const checkClosed = setInterval(() => {
      if (popup.closed) {
        clearInterval(checkClosed);
        window.removeEventListener('message', handler);
      }
    }, 300);
  }

  /** Vérifie l'état de connexion via /me. Appelé au chargement et après callback. */
  checkAndLoadProfile(): void {
    this._loading.set(true);
    this._errorMessage.set(null);

    this.http.get<FrontierMeResponse>('/api/user/me').subscribe({
      next: (res) => {
        this._loading.set(false);
        if (res.connected) {
          this._profile.set({
            frontierCustomerId: res.customerId ?? '',
            commanderName: res.commander ?? '',
            squadronName: res.squadron ?? null,
            lastSystemName: null,
            shipName: null,
            guildId: res.guildId ?? null,
            guildName: res.guildName ?? null,
            lastFetchedAt: new Date().toISOString(),
          });
          this._state.set('connected');
        } else {
          this._profile.set(null);
          this._state.set('not-connected');
        }
      },
      error: (err) => {
        this._loading.set(false);
        this._state.set('error');
        this._errorMessage.set(err?.error?.message ?? err?.message ?? 'Erreur');
      },
    });
  }

  /** Déconnexion Frontier : efface le token côté backend et l'état local. */
  logout(): void {
    this.http.post(`${this.base}/logout`, {}).subscribe({
      next: () => this.reset(),
      error: () => this.reset(),
    });
  }

  /** Définit l'état erreur avec un message (ex. après redirect callback). */
  setError(message: string): void {
    this._state.set('error');
    this._errorMessage.set(message);
    this._profile.set(null);
  }

  /** Réinitialise l'état après une erreur (avant de relancer le login). */
  reset(): void {
    this._state.set('not-connected');
    this._profile.set(null);
    this._errorMessage.set(null);
  }
}
