import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import type { BridgeRoute, BridgeRoutePoint } from '@elite-bridge-shared/bridge-planner-route';
import { environment } from '../../../environments/environment';

/**
 * Dernière route BridgePlanner stockée sur le serveur EliteBridgePlanner (7293).
 * Ne passe pas par le proxy `/api` du serveur 501.
 */
@Injectable({ providedIn: 'root' })
export class BridgeRouteApiService {
  private readonly http = inject(HttpClient);

  private bridgeRouteUrl(): string {
    const base = environment.bridgePlannerApiUrl.replace(/\/$/, '');
    return `${base}/api/bridge-route`;
  }

  getLatest(): Observable<BridgeRoute | null> {
    const url = this.bridgeRouteUrl();
    return this.http.get<unknown>(url).pipe(
      map((raw) => this.normalizeBridgeRoute(raw)),
      tap((data) => {
        console.debug('[BridgeRouteApi] GET après normalisation', {
          url,
          pointCount: data?.points?.length ?? 0,
          keysSample: data ? Object.keys(data) : [],
        });
      }),
      catchError((err: unknown) => {
        if (err instanceof HttpErrorResponse && err.status === 404) return of(null);
        this.logFailure('GET', url, err);
        return throwError(() => err);
      }),
    );
  }

  /** Efface la route côté BridgePlanner (optionnel). */
  clear(): Observable<void> {
    const url = this.bridgeRouteUrl();
    return this.http.delete<void>(url).pipe(
      catchError((err: unknown) => {
        this.logFailure('DELETE', url, err);
        return throwError(() => err);
      }),
    );
  }

  /**
   * Garantit `points` + champs camelCase même si l’API renvoie du PascalCase
   * ou si le navigateur conserve des clés inattendues.
   */
  private normalizeBridgeRoute(raw: unknown): BridgeRoute | null {
    if (raw == null || typeof raw !== 'object') return null;
    const o = raw as Record<string, unknown>;
    const rawPoints = o['points'] ?? o['Points'];
    if (!Array.isArray(rawPoints)) return null;
    const points: BridgeRoutePoint[] = rawPoints.map((rp, i) => this.normalizePoint(rp, i));
    const src = o['source'] ?? o['Source'];
    return {
      points,
      source: typeof src === 'string' ? src : undefined,
    };
  }

  private normalizePoint(rp: unknown, i: number): BridgeRoutePoint {
    const p = rp as Record<string, unknown>;
    const lat = Number(p['lat'] ?? p['Lat']);
    const lng = Number(p['lng'] ?? p['Lng']);
    const y = Number(p['y'] ?? p['Y'] ?? 0);
    const id = String(p['id'] ?? p['Id'] ?? `bp-${i}`);
    const type = this.normalizePointKind(String(p['type'] ?? p['Type'] ?? 'tablier'));
    const colorHex = String(p['colorHex'] ?? p['ColorHex'] ?? '#00d4ff');
    return { id, lat, lng, y, type, colorHex };
  }

  private normalizePointKind(s: string): BridgeRoutePoint['type'] {
    const u = s.trim().toLowerCase();
    const allowed: BridgeRoutePoint['type'][] = [
      'pile',
      'tablier',
      'systeme_operationnel',
      'debut',
      'fin',
    ];
    return (allowed as string[]).includes(u) ? (u as BridgeRoutePoint['type']) : 'tablier';
  }

  private logFailure(method: string, url: string, err: unknown): void {
    const status = err instanceof HttpErrorResponse ? err.status : undefined;
    const message = err instanceof HttpErrorResponse ? err.message : String(err);
    console.error(`[BridgeRouteApi] ${method} échoué — URL: ${url}`, { status, message, err });
  }
}
