import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, forkJoin, of, throwError } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { buildRoutePayloadFromSystems } from '@elite-bridge-shared/bridge-planner-route';
import { BridgeStore } from './bridge.store';
import { EdsmSystemCoordsService } from './edsm-system-coords.service';
import type { StarSystemDto } from '../models/models';

@Injectable({ providedIn: 'root' })
export class BridgeRoute501Service {
  private readonly http = inject(HttpClient);
  private readonly store = inject(BridgeStore);
  private readonly edsm = inject(EdsmSystemCoordsService);

  /**
   * Publie le pont actif sur ce même backend (`POST /api/bridge-route`) pour le dashboard 501.
   * Couleurs : `shared/bridge-planner-route.ts` (thème bleu BridgePlanner).
   */
  sendActiveBridgeTo501(): Observable<{ ok: true; pointCount: number }> {
    const bridge = this.store.activeBridge();
    const systems = bridge?.systems?.length
      ? [...bridge.systems].sort((a, b) => a.order - b.order)
      : [];
    if (systems.length === 0) {
      return throwError(() => new Error('Aucun système dans le pont actif.'));
    }

    return forkJoin(
      systems.map((s) =>
        this.edsm.getCoords(s.name).pipe(
          catchError(() => of(null)),
        ),
      ),
    ).pipe(
      switchMap((coordsList) => {
        const payload = buildRoutePayloadFromSystems(
          systems as StarSystemDto[],
          coordsList,
        );
        if (payload.points.length === 0) {
          return throwError(() => new Error('Coordonnées EDSM introuvables pour les systèmes du pont.'));
        }
        payload.source = bridge?.name ?? 'BridgePlanner';
        return this.http.post<{ received?: number }>('/api/bridge-route', payload).pipe(
          map(() => ({ ok: true as const, pointCount: payload.points.length })),
        );
      }),
    );
  }
}
