import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { tap, catchError, of } from 'rxjs';
import type { CurrentGuildDto } from '../models/current-guild.model';

@Injectable({ providedIn: 'root' })
export class CurrentGuildService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  private readonly _guild = signal<CurrentGuildDto | null>(null);
  private readonly _loaded = signal(false);

  readonly guild = this._guild.asReadonly();
  readonly loaded = this._loaded.asReadonly();
  readonly guildId = computed(() => this._guild()?.id ?? 0);

  /**
   * Charge la guilde courante depuis GET /api/guild/current.
   * À appeler au démarrage de l'app (APP_INITIALIZER).
   */
  load(): void {
    if (this._loaded()) return;
    this._loaded.set(true);
    this.http
      .get<CurrentGuildDto>(`${this.base}/guild/current`)
      .pipe(
        tap((g) => this._guild.set(g)),
        catchError((err) => {
          console.error('[CurrentGuildService] Erreur chargement guilde courante', err);
          this._loaded.set(true);
          return of(null);
        }),
      )
      .subscribe();
  }

  /** Pour APP_INITIALIZER : charge et retourne une Promise. */
  loadAsync(): Promise<void> {
    if (this._loaded())
      return Promise.resolve();

    this._loaded.set(true);
    return new Promise((resolve) => {
      this.http
        .get<CurrentGuildDto>(`${this.base}/guild/current`)
        .pipe(
          tap((g) => this._guild.set(g)),
          catchError((err) => {
            console.error('[CurrentGuildService] Erreur chargement guilde courante', err);
            return of(null);
          }),
        )
        .subscribe(() => resolve());
    });
  }
}
