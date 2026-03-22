/**
 * Feature archived – no reliable external data source for Faction → Systems → Influence %.
 * Conserver pour R&D futur. Voir docs/GUILD-SYSTEMS.md.
 */
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { GuildSystemsResponseDto } from '../models/guild-systems.model';

/** Base API. Sur localhost:4200, appel direct au backend car le proxy peut échouer. */
function getApiBase(): string {
  if (typeof window !== 'undefined' && /^https?:\/\/localhost:4200(\/|$)/.test(window.location.origin)) {
    return 'https://localhost:7294/api';
  }
  return '/api';
}
const API_BASE = getApiBase();

/**
 * Service Guild Systems — le frontend déclenche uniquement.
 * Le backend utilise la guilde courante (Guild:CurrentGuildId).
 */
@Injectable({ providedIn: 'root' })
export class GuildSystemsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = API_BASE;

  getSystems(): Observable<GuildSystemsResponseDto> {
    return this.http.get<GuildSystemsResponseDto>(`${this.base}/guild/systems`);
  }

  resetSystems(): Observable<{ deletedGuildSystems: number; deletedControlledSystems: number }> {
    return this.http.post<{ deletedGuildSystems: number; deletedControlledSystems: number }>(
      `${this.base}/guild/systems/reset`,
      {}
    );
  }

  toggleHeadquarter(systemId: number): Observable<void> {
    return this.http.post<void>(`${this.base}/guild/systems/${systemId}/toggle-headquarter`, {});
  }

  syncBgs(): Observable<{ updated: number }> {
    return this.http.post<{ updated: number }>(`${this.base}/guild/systems/sync`, {});
  }
}
