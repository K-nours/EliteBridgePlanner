import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

/**
 * Service de test pour explorer les APIs externes (EDSM, Spansh).
 * Zone d'expérimentation — ne pas utiliser en production.
 */
@Injectable({ providedIn: 'root' })
export class ApiExplorerService {
  private readonly http = inject(HttpClient);

  private readonly EDSM_BASE = 'https://www.edsm.net/api-v1';

  /**
   * URL Spansh pour le plotter galaxy.
   * ⚠️ À CONFIRMER : api.spansh.co.uk n'existe pas (ERR_NAME_NOT_RESOLVED).
   * L'endpoint exact doit être vérifié via l'onglet Network des DevTools du navigateur
   * en utilisant le site Spansh (spansh.co.uk ou beta.spansh.co.uk).
   * Voir la procédure en fin de fichier.
   */
  private readonly SPANSH_PLOTTER_URL = 'https://spansh.co.uk/api/galaxy/plot';

  /** API Spansh Colonisation — proxifié en dev pour contourner CORS */
  private readonly SPANSH_COLONISATION_URL = '/spansh-api/api/colonisation/route';

  /**
   * Teste l'API EDSM — récupère les infos d'un système.
   * GET https://www.edsm.net/api-v1/system
   */
  testEdsnSystem(systemName: string): Observable<unknown> {
    const url = `${this.EDSM_BASE}/system`;
    const params = {
      systemName,
      showId: '1',
      showCoordinates: '1',
      showInformation: '1'
    };
    return this.http.get(url, { params }).pipe(
      tap((response) => {
        console.log('[ApiExplorer] EDSM system:', systemName, response);
      })
    );
  }

  /**
   * Teste l'API EDSM — récupère les corps célestes d'un système.
   * GET https://www.edsm.net/api-system-v1/bodies
   */
  testEdsnBodies(systemName: string): Observable<unknown> {
    const url = 'https://www.edsm.net/api-system-v1/bodies';
    return this.http.get(url, { params: { systemName } }).pipe(
      tap((response) => {
        console.log('[ApiExplorer] EDSM bodies:', response);
      })
    );
  }

  /**
   * Teste l'API Spansh — requête de plot galaxy (route entre deux systèmes).
   * Méthode isolée : modifier SPANSH_PLOTTER_URL et/ou le body selon les requêtes
   * observées dans l'onglet Network du site Spansh.
   */
  testSpanshRoute(systemName: string): Observable<unknown> {
    const body = {
      source: systemName,
      destination: 'Colonia',
      range: 50,
      efficiency: 100
    };
    return this.http.post<unknown>(this.SPANSH_PLOTTER_URL, body).pipe(
      tap((response) => {
        console.log('[ApiExplorer] Spansh galaxy plot:', systemName, '-> Colonia', response);
      })
    );
  }

  /**
   * Teste l'API Spansh Colonisation Plotter.
   * POST form-urlencoded : source_system, destination_system
   * Retourne { job: "...", status: "queued" } — utiliser getSpanshColonisationResult(job) pour le résultat.
   */
  testSpanshColonisationRoute(source: string, destination: string): Observable<unknown> {
    const body = new HttpParams()
      .set('source_system', source)
      .set('destination_system', destination);
    return this.http
      .post<unknown>(this.SPANSH_COLONISATION_URL, body, {
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' }
      })
      .pipe(
        tap((response) => {
          console.log('[ApiExplorer] Spansh route:', response);
        })
      );
  }

  /**
   * Récupère le résultat d'une route colonisation Spansh (étape 2 après testSpanshColonisationRoute).
   * GET /spansh-api/api/colonisation/results/{jobId}
   */
  getSpanshColonisationResult(jobId: string): Observable<unknown> {
    const url = `/spansh-api/api/colonisation/route`;
    return this.http.get<unknown>(url, { params: { job: jobId } }).pipe(
      tap((response) => {
        console.log('[ApiExplorer] Spansh result:', response);
      })
    );
  }
}

/*
 * ── PROCÉDURE : Retrouver l'endpoint Spansh dans les DevTools ─────────────────────
 *
 * 1. Ouvrir le site Spansh :
 *    - https://spansh.co.uk/exact-plotter  OU
 *    - https://beta.spansh.co.uk/plotter
 *
 * 2. Ouvrir les DevTools (F12 ou Cmd+Option+I sur Mac)
 *
 * 3. Onglet Network :
 *    - Cliquer sur "Network" / "Réseau"
 *    - Cocher "Preserve log" pour conserver l'historique après navigation
 *    - Filtrer par "Fetch/XHR" pour ne voir que les appels API (optionnel)
 *
 * 4. Effectuer une action de plot :
 *    - Remplir les champs (source, destination, jump range)
 *    - Cliquer sur "Calculate" / "Calculer"
 *
 * 5. Repérer la requête :
 *    - Chercher une requête POST (méthode en violet/gras)
 *    - Regarder l'URL complète dans la colonne "Name" ou "Request URL"
 *    - Cliquer dessus pour voir :
 *      - Request URL (c'est l'endpoint à copier)
 *      - Request Method (POST ou GET)
 *      - Payload / Request Body (structure JSON attendue)
 *
 * 6. Mettre à jour ce fichier :
 *    - SPANSH_PLOTTER_URL = Request URL
 *    - body dans testSpanshRoute() = structure du Payload
 */
