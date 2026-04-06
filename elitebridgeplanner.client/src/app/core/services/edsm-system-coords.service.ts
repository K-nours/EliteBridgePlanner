import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, of } from 'rxjs';
import { map } from 'rxjs/operators';

/** Réponse minimale EDSM system (coords). */
function parseEdsmSystemCoords(body: unknown): { x: number; y: number; z: number } | null {
  const o = body as { coords?: unknown };
  const raw = o?.coords;
  if (raw == null || raw === false || typeof raw !== 'object') return null;
  const c = raw as { x?: unknown; y?: unknown; z?: unknown };
  const x = Number(c.x);
  const y = Number(c.y);
  const z = Number(c.z);
  if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(z)) return null;
  return { x, y, z };
}

@Injectable({ providedIn: 'root' })
export class EdsmSystemCoordsService {
  private readonly http = inject(HttpClient);
  private readonly base = 'https://www.edsm.net/api-v1/system';

  /** Coordonnées galaxie ED (Ly) pour un nom de système. */
  getCoords(systemName: string): Observable<{ x: number; y: number; z: number } | null> {
    const name = systemName?.trim();
    if (!name) return of(null);
    return this.http
      .get<unknown>(this.base, {
        params: { systemName: name, showCoordinates: '1', showId: '0' },
      })
      .pipe(map((body) => parseEdsmSystemCoords(body)));
  }
}
