import { Injectable, inject, signal, computed, isDevMode } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { tap, catchError, of } from 'rxjs';

export interface InaraApiSettingsClientDto {
  apiKeyConfigured: boolean;
}
import type { GuildSettingsDto, GuildSettingsUpdateDto } from '../models/guild-settings.model';

/** Base API. En dev, URL explicite si proxy Vite inactif. À retirer une fois proxy confirmé. */
const API_BASE = isDevMode() ? 'https://localhost:7294/api' : '/api';

@Injectable({ providedIn: 'root' })
export class GuildSettingsService {
  private readonly http = inject(HttpClient);
  private readonly base = API_BASE;

  private readonly _settings = signal<GuildSettingsDto | null>(null);
  private readonly _loaded = signal(false);

  readonly settings = this._settings.asReadonly();
  readonly loaded = this._loaded.asReadonly();
  readonly inaraFactionPresenceUrl = computed(() => this._settings()?.inaraFactionPresenceUrl ?? null);
  readonly inaraSquadronUrl = computed(() => this._settings()?.inaraSquadronUrl ?? null);
  readonly inaraCmdrUrl = computed(() => this._settings()?.inaraCmdrUrl ?? null);
  readonly lastSystemsImportAt = computed(() => this._settings()?.lastSystemsImportAt ?? null);
  readonly lastCommandersSyncAt = computed(() => this._settings()?.lastCommandersSyncAt ?? null);
  readonly lastAvatarImportAt = computed(() => this._settings()?.lastAvatarImportAt ?? null);

  load(): void {
    this.http
      .get<GuildSettingsDto>(`${this.base}/guild/settings`)
      .pipe(
        tap((s) => {
          this._settings.set(s);
          this._loaded.set(true);
        }),
        catchError((err) => {
          console.error('[GuildSettingsService] Erreur chargement settings', err);
          this._loaded.set(true);
          return of(null);
        }),
      )
      .subscribe();
  }

  update(payload: GuildSettingsUpdateDto) {
    return this.http.put<void>(`${this.base}/guild/settings`, payload).pipe(tap(() => this.load()));
  }

  getInaraApiSettings() {
    return this.http.get<InaraApiSettingsClientDto>(`${this.base}/guild/settings/inara-api`);
  }

  putInaraApiSettings(body: { apiKey?: string }) {
    return this.http.put<{ saved: boolean }>(`${this.base}/guild/settings/inara-api`, body);
  }

  /** Rafraîchit lastSystemsImportAt (après import réussi). */
  refreshLastSystemsImportAt(isoDate: string | null): void {
    this._settings.update((s) => (s ? { ...s, lastSystemsImportAt: isoDate } : s));
  }

  /** Rafraîchit lastCommandersSyncAt (après sync réussi). */
  refreshLastCommandersSyncAt(isoDate: string | null): void {
    this._settings.update((s) => (s ? { ...s, lastCommandersSyncAt: isoDate } : s));
  }

  /** Rafraîchit lastAvatarImportAt (après import avatar réussi). */
  refreshLastAvatarImportAt(isoDate: string | null): void {
    this._settings.update((s) => (s ? { ...s, lastAvatarImportAt: isoDate } : s));
  }
}
