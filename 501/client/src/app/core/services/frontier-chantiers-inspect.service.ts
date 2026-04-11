import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { FrontierChantiersInspectResponse } from '../models/frontier-chantiers-inspect.model';
import { API_BASE_URL, buildHttpUrl } from '../config/api-base-url';

/**
 * Inspection des données Frontier déjà disponibles via CAPI /profile (même source que le dashboard).
 * Pas d’OAuth supplémentaire — le token serveur doit déjà exister.
 *
 * URL : `buildHttpUrl(API_BASE_URL, '/api/integrations/frontier/chantiers-inspect')`.
 * En dev, `API_BASE_URL` vient de `environment.guildDashboardApiBase` (http://localhost:5294) ;
 * les requêtes relatives `/api/...` sont aussi réécrites par `apiBaseUrlInterceptor` (voir app.config).
 */
@Injectable({ providedIn: 'root' })
export class FrontierChantiersInspectService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  private readonly inspectPath = '/api/integrations/frontier/chantiers-inspect';

  /** URL effective pour les logs utilisateur (relative si pas de base, sinon absolue vers le backend). */
  getInspectRequestUrl(): string {
    return buildHttpUrl(this.apiBaseUrl, this.inspectPath);
  }

  inspect(): Observable<FrontierChantiersInspectResponse> {
    return this.http.get<FrontierChantiersInspectResponse>(this.getInspectRequestUrl());
  }
}
