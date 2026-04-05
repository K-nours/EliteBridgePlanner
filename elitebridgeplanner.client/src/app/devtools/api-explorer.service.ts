import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { tap, catchError, retry } from 'rxjs/operators';

import type { EnrichmentMode } from './enrichment-types';
import type { ColonisationRouteAnalysis } from './colonisation-route.analyzer';

/** Session persistante : dernière route calculée (conservée au changement de page) */
export interface PersistedRouteSession {
  colonisationResult: unknown;
  routeAnalysis: ColonisationRouteAnalysis;
  source: string | null;
  destination: string | null;
  statusText: string;
}

/**
 * Service de test pour explorer les APIs externes (EDSM, Spansh).
 * Zone d'expérimentation — ne pas utiliser en production.
 */
@Injectable({ providedIn: 'root' })
export class ApiExplorerService {
  private readonly http = inject(HttpClient);

  private readonly EDSM_BASE = 'https://www.edsm.net/api-v1';

  /** Mode d'enrichissement : edsm | spansh-lite | hybrid | comparison */
  enrichmentMode: EnrichmentMode = 'edsm';

  /** En mode hybrid : nombre de meilleurs candidats par dépôt à enrichir avec EDSM (défaut 3) */
  hybridTopN = 3;

  /** Cache en mémoire pour EDSM bodies (clé = systemName normalisé) */
  private readonly edsmBodiesCache = new Map<string, unknown>();

  /** Statistiques cache pour debug (hits, misses) */
  readonly edsmCacheStats = { hits: 0, misses: 0 };

  /**
   * Bypass temporaire du cache pour debug.
   * false = appel EDSM direct sans cache (pour comparer avec/sans cache).
   */
  useEdsmCache = true;

  /** Dernière route calculée — persistante au changement de page */
  lastRouteSession: PersistedRouteSession | null = null;

  saveRouteSession(session: PersistedRouteSession): void {
    this.lastRouteSession = session;
  }

  clearRouteSession(): void {
    this.lastRouteSession = null;
  }

  /** Réinitialise le cache (utile entre analyses) */
  clearEdsmBodiesCache(): void {
    this.edsmBodiesCache.clear();
    this.edsmCacheStats.hits = 0;
    this.edsmCacheStats.misses = 0;
    console.log('[EDSM cache] cache vidé, stats réinitialisées');
  }

  /** Clé de cache normalisée (trim + toLowerCase) — source unique pour lecture et écriture */
  normalizeCacheKey(systemName: string): string {
    return (systemName?.trim() ?? '').toLowerCase();
  }

  /** Comptabilise un hit sur le cache global (quand getOrFetchEdsmSummary sert depuis le cache sans appeler EDSM) */
  recordGlobalCacheHit(): void {
    this.edsmCacheStats.hits++;
  }

  /** Comptabilise les systèmes filtrés au planification (déjà en cache, jamais passés à getOrFetchEdsmSummary) */
  recordPlannedCacheHits(count: number): void {
    this.edsmCacheStats.hits += count;
  }

  /**
   * URL Spansh pour le plotter galaxy.
   * ⚠️ À CONFIRMER : api.spansh.co.uk n'existe pas (ERR_NAME_NOT_RESOLVED).
   * L'endpoint exact doit être vérifié via l'onglet Network des DevTools du navigateur
   * en utilisant le site Spansh (spansh.co.uk ou beta.spansh.co.uk).
   * Voir la procédure en fin de fichier.
   */
  private readonly SPANSH_PLOTTER_URL = 'https://spansh.co.uk/api/galaxy/plot';

  /** API Spansh Colonisation — proxy backend (SpanshProxyController), JWT requis. Même chemin avec ng serve (/api → ASP.NET). */
  private readonly SPANSH_COLONISATION_URL = '/api/spansh/colonisation/route';

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
   * Récupère les systèmes dans une sphère (50 LY) autour de coordonnées.
   * GET https://www.edsm.net/api-v1/sphere-systems
   */
  getEdsmSphereSystemsByCoords(x: number, y: number, z: number, radius = 50): Observable<{ distance: number; name: string }[]> {
    const url = `${this.EDSM_BASE}/sphere-systems`;
    const params = { x: String(x), y: String(y), z: String(z), radius: String(radius) };
    return this.http.get<{ distance: number; name: string }[]>(url, { params }).pipe(
      tap((r) => console.log('[ApiExplorer] EDSM sphere-systems:', r?.length ?? 0, 'systèmes'))
    );
  }

  /**
   * Récupère les systèmes dans une sphère (50 LY) autour d'un système (par nom).
   * GET https://www.edsm.net/api-v1/sphere-systems
   */
  getEdsmSphereSystemsByName(systemName: string, radius = 50): Observable<{ distance: number; name: string }[]> {
    const url = `${this.EDSM_BASE}/sphere-systems`;
    const params = { systemName, radius: String(radius) };
    return this.http.get<{ distance: number; name: string }[]>(url, { params }).pipe(
      tap((r) => console.log('[ApiExplorer] EDSM sphere-systems:', r?.length ?? 0, 'systèmes'))
    );
  }

  /**
   * Récupère les corps célestes d'un système (avec cache).
   * GET https://www.edsm.net/api-system-v1/bodies
   */
  testEdsnBodies(systemName: string): Observable<unknown> {
    const key = this.normalizeCacheKey(systemName);
    console.log('[EDSM cache] READ key:', JSON.stringify(key), '| raw:', JSON.stringify(systemName));

    if (this.useEdsmCache) {
      const cached = this.edsmBodiesCache.get(key);
      if (cached !== undefined) {
        this.edsmCacheStats.hits++;
        console.log('[EDSM cache] HIT  key:', JSON.stringify(key), '| stats:', this.edsmCacheStats.hits, 'hits,', this.edsmCacheStats.misses, 'misses');
        return of(cached);
      }
      this.edsmCacheStats.misses++;
      console.log('[EDSM cache] MISS key:', JSON.stringify(key), '| stats:', this.edsmCacheStats.hits, 'hits,', this.edsmCacheStats.misses, 'misses');
    } else {
      console.log('[EDSM cache] BYPASS useEdsmCache=false, appel EDSM direct');
    }

    const url = 'https://www.edsm.net/api-system-v1/bodies';
    console.log('[EDSM cache] APPEL EDSM RÉEL key:', JSON.stringify(key), '| systemName:', JSON.stringify(systemName));
    return this.http.get(url, { params: { systemName } }).pipe(
      retry(1),
      tap((response) => {
        if (this.useEdsmCache) {
          this.edsmBodiesCache.set(key, response);
          console.log('[EDSM cache] WRITE key:', JSON.stringify(key), '| bodyCount:', (response as { bodyCount?: number })?.bodyCount, '| Map size:', this.edsmBodiesCache.size);
        }
      }),
      catchError((err) => {
        console.warn('[EDSM cache] erreur EDSM key:', JSON.stringify(key), err);
        const empty: unknown = { bodyCount: 0, bodies: [] };
        if (this.useEdsmCache) {
          this.edsmBodiesCache.set(key, empty);
          console.log('[EDSM cache] WRITE (empty/fallback) key:', JSON.stringify(key));
        }
        return of(empty);
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
   * Récupère le résultat d'une route colonisation Spansh (étape 2).
   * GET /api/spansh/results/{jobId}
   */
  getSpanshColonisationResult(jobId: string): Observable<unknown> {
    const url = `/api/spansh/results/${jobId}`;
    return this.http.get<unknown>(url).pipe(
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
