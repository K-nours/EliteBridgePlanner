import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type {
  DeclaredChantierListItemApi,
  DeclaredChantierPersistBody,
  DeclaredChantierRefreshAllResultApi,
} from '../models/declared-chantiers-api.model';
import { API_BASE_URL, buildHttpUrl } from '../config/api-base-url';

@Injectable({ providedIn: 'root' })
export class DeclaredChantiersApiService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  private readonly path = '/api/integrations/frontier/chantiers-declared';

  listUrl(): string {
    return buildHttpUrl(this.apiBaseUrl, this.path);
  }

  /** Chantiers actifs du CMDR courant (profil Frontier). */
  listMine(): Observable<DeclaredChantierListItemApi[]> {
    return this.http.get<DeclaredChantierListItemApi[]>(buildHttpUrl(this.apiBaseUrl, `${this.path}/me`));
  }

  /** Chantiers actifs des autres CMDRs de la guilde. */
  listOthers(): Observable<DeclaredChantierListItemApi[]> {
    return this.http.get<DeclaredChantierListItemApi[]>(buildHttpUrl(this.apiBaseUrl, `${this.path}/others`));
  }

  /** Liste complète (guilde), sans filtre CMDR — usage ponctuel / compat. */
  listActive(): Observable<DeclaredChantierListItemApi[]> {
    return this.http.get<DeclaredChantierListItemApi[]>(this.listUrl());
  }

  persist(body: DeclaredChantierPersistBody): Observable<DeclaredChantierListItemApi> {
    return this.http.post<DeclaredChantierListItemApi>(this.listUrl(), body);
  }

  /** Rafraîchit tous les chantiers actifs (CAPI /market + /market?marketId=…). */
  refreshAll(): Observable<DeclaredChantierRefreshAllResultApi> {
    return this.http.post<DeclaredChantierRefreshAllResultApi>(
      buildHttpUrl(this.apiBaseUrl, `${this.path}/refresh-all`),
      {},
    );
  }

  /** Rafraîchit un chantier par id SQL. */
  refreshOne(id: number): Observable<DeclaredChantierListItemApi> {
    return this.http.post<DeclaredChantierListItemApi>(
      buildHttpUrl(this.apiBaseUrl, `${this.path}/refresh-one`),
      { id },
    );
  }

  /** Suppression définitive en base (204 No Content). */
  delete(id: number): Observable<void> {
    return this.http.delete<void>(buildHttpUrl(this.apiBaseUrl, `${this.path}/${id}`));
  }
}
