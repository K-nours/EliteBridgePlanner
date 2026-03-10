import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  BridgeDto, CreateBridgeRequest,
  StarSystemDto, CreateSystemRequest,
  UpdateSystemRequest, MoveSystemRequest
} from '../models/models';

/**
 * Service d'accès à l'API .NET.
 * Une seule responsabilité : les appels HTTP.
 * La logique métier et l'état sont dans BridgeStore.
 */
@Injectable({ providedIn: 'root' })
export class BridgeApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  // ── Bridges ──────────────────────────────────────────────────────────────

  getBridges(): Observable<BridgeDto[]> {
    return this.http.get<BridgeDto[]>(`${this.baseUrl}/bridges`);
  }

  getBridgeById(id: number): Observable<BridgeDto> {
    return this.http.get<BridgeDto>(`${this.baseUrl}/bridges/${id}`);
  }

  clearAllSystems(bridgeId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/bridges/${bridgeId}/systems`);
  }

  createBridge(request: CreateBridgeRequest): Observable<BridgeDto> {
    return this.http.post<BridgeDto>(`${this.baseUrl}/bridges`, request);
  }

  importRoute(systems: { name: string; type: string }[], options?: { bridgeName?: string; replaceBridgeId?: number }): Observable<BridgeDto> {
    const body = {
      systems: systems.map(s => ({ name: s.name, type: s.type })),
      bridgeName: options?.bridgeName,
      replaceBridgeId: options?.replaceBridgeId
    };
    return this.http.post<BridgeDto>(`${this.baseUrl}/bridges/import-route`, body);
  }

  /** Import via endpoint dev (source/destination) — fonctionne sans JWT, utilise l'utilisateur démo. */
  importSpanshBySourceDest(source: string, destination: string): Observable<{ bridge: BridgeDto }> {
    return this.http.post<{ bridge: BridgeDto }>(`${this.baseUrl}/dev/import-spansh`, { source, destination });
  }

  // ── Systems ───────────────────────────────────────────────────────────────

  addSystem(request: CreateSystemRequest): Observable<StarSystemDto> {
    return this.http.post<StarSystemDto>(`${this.baseUrl}/systems`, request);
  }

  updateSystem(id: number, request: UpdateSystemRequest): Observable<StarSystemDto> {
    return this.http.patch<StarSystemDto>(`${this.baseUrl}/systems/${id}`, request);
  }

  reorderSystem(id: number, request: MoveSystemRequest): Observable<StarSystemDto> {
    return this.http.patch<StarSystemDto>(`${this.baseUrl}/systems/${id}/reorder`, request);
  }

  // moveSystem(id: number, insertAfterId: number | null): Observable<StarSystemDto> {
  //   return this.http.patch<StarSystemDto>(
  //     `${this.baseUrl}/systems/${id}/move`,
  //     { insertAfterId }
  //   );
  // }

  deleteSystem(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/systems/${id}`);
  }
}
