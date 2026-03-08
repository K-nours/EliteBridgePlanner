import { Component, inject, ChangeDetectorRef, OnDestroy, ViewChild, ElementRef, AfterViewChecked } from '@angular/core';
import { DecimalPipe, JsonPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { from, of, Observable } from 'rxjs';
import { concatMap, delay, catchError, finalize, map, tap, toArray } from 'rxjs/operators';
import { ApiExplorerService } from './api-explorer.service';
import { analyzeColonisationRoute, getCandidatesInWindow, PILE_WINDOW_FALLBACK_LY, PILE_WINDOW_LY, type ColonisationRouteAnalysis, type LocalPileCandidate, type SpanshJump } from './colonisation-route.analyzer';
import {
  buildSpanshLiteSummary,
  computeScoreFromAnalysis,
  computeSpanshPreScore,
  edsBodiesToAnalysisSummary,
  type CandidateAnalysisSummary,
  type EnrichedCandidate,
  type EnrichmentMode,
  isEnrichmentKnown
} from './enrichment-types';

interface EdsBody {
  subType?: string;
  isLandable?: boolean;
}

interface EdsBodiesData {
  bodyCount?: number;
  bodies?: EdsBody[];
}

/**
 * Résumé EDSM Bodies pour un candidat enrichi.
 * Prépare le terrain pour le scoring PILE (priorité: mondes exploitables rocheux/riches en métaux).
 *
 * Hiérarchie souhaitée pour le scoring :
 * 1. rockyMetalRichLandableCount (= metalRichLandableCount, "Metal-rich body" landable)
 * 2. metalRichLandableCount
 * 3. highMetalContentLandableCount
 * 4. bodyCount
 * 5. waterWorldCount
 * 6. earthLikeWorldCount
 * 7. landmarkValue (Spansh)
 */
export interface EdsBodiesSummary {
  bodyCount: number;
  landable: number;
  metalRich: number;
  highMetalContent: number;
  waterWorld: number;
  earthLike: number;
  /** Rocky body (exclut Rocky Ice world) — subType "Rocky body" */
  rockyCount: number;
  rockyLandableCount: number;
  /** Metal-rich body — subType "Metal-rich" */
  metalRichCount: number;
  metalRichLandableCount: number;
  /** High metal content world — subType "High metal content" */
  highMetalContentCount: number;
  highMetalContentLandableCount: number;
  /** Alias pour metalRichLandableCount (priorité #1 du scoring) */
  rockyMetalRichLandableCount: number;
}

/** Résultat EDSM pour un candidat : données ou inconnu (legacy, cf. EnrichedCandidate) */
export type EnrichedCandidateEdsm = EdsBodiesSummary | { unknown: true };

/** État d'enrichissement EDSM d'un dépôt */
export type DepositEnrichmentState = 'not_started' | 'loading' | 'done' | 'no_data' | 'error';

/** Ligne du tableau de comparaison (un dépôt) */
export interface ComparisonRow {
  pileIndex: number;
  edsm: { name: string; score: number } | null;
  spanshLite: { name: string; score: number } | null;
  hybrid: { name: string; score: number } | null;
  hasDifferences: boolean;
}

/** Métriques globales de comparaison */
export interface ComparisonMetrics {
  totalTimeMs: number;
  edsmCalls: number;
  enrichedCount: number;
  noDataCount: number;
}

/**
 * Calcule le score métier d'un candidat PILE enrichi.
 * Hiérarchie : metalRichLand > rockyLand > hmcLand > bodyCount > waterWorld > earthLike > landmarkValue
 */
export function computePileCandidateScore(
  edsm: EdsBodiesSummary,
  landmarkValue: number | undefined
): number {
  const lv = typeof landmarkValue === 'number' && !Number.isNaN(landmarkValue) ? landmarkValue : 0;
  return (
    edsm.metalRichLandableCount * 100 +
    edsm.rockyLandableCount * 40 +
    edsm.highMetalContentLandableCount * 25 +
    edsm.bodyCount * 8 +
    edsm.waterWorld * 20 +
    edsm.earthLike * 15 +
    lv * 0.01
  );
}

@Component({
  selector: 'app-api-explorer-demo',
  standalone: true,
  imports: [DecimalPipe, JsonPipe, FormsModule],
  templateUrl: './api-explorer-demo.component.html',
  styleUrl: './api-explorer-demo.component.scss'
})
export class ApiExplorerDemoComponent implements OnDestroy, AfterViewChecked {
  private readonly apiExplorer = inject(ApiExplorerService);

  /** Statistiques cache EDSM (pour affichage debug) */
  get edsmCacheStats(): { hits: number; misses: number } {
    return this.apiExplorer.edsmCacheStats;
  }
  /** Texte du tooltip indicateur cache EDSM (mis à jour à chaque détection) */
  get edsmCacheTooltip(): string {
    const n = this.globalEdsmSummaryCache.size;
    const { hits, misses } = this.edsmCacheStats;
    return `Cache EDSM\n${n} système${n !== 1 ? 's' : ''} en mémoire\n${hits} hit(s), ${misses} miss(es) cette session`;
  }
  /** Mode d'enrichissement : edsm | spansh-lite | hybrid */
  get enrichmentMode(): EnrichmentMode {
    return this.apiExplorer.enrichmentMode;
  }
  set enrichmentMode(v: EnrichmentMode) {
    this.apiExplorer.enrichmentMode = v;
    this.cdr.detectChanges();
  }
  /** En mode hybrid : nombre de meilleurs candidats par dépôt à enrichir avec EDSM */
  get hybridTopN(): number {
    return this.apiExplorer.hybridTopN;
  }
  set hybridTopN(v: number) {
    this.apiExplorer.hybridTopN = v;
    this.cdr.detectChanges();
  }
  /** Bypass cache EDSM pour debug (désactiver = appels directs sans cache) */
  get useEdsmCache(): boolean {
    return this.apiExplorer.useEdsmCache;
  }
  set useEdsmCache(v: boolean) {
    this.apiExplorer.useEdsmCache = v;
    this.cdr.detectChanges();
  }
  /** Message temporaire après vidage du cache */
  cacheClearedFeedback: string | null = null;

  clearEdsmCache(): void {
    this.apiExplorer.clearEdsmBodiesCache();
    this.globalEdsmSummaryCache.clear();
    this.cacheClearedFeedback = 'Cache vidé';
    this.cdr.detectChanges();
    setTimeout(() => {
      this.cacheClearedFeedback = null;
      this.cdr.detectChanges();
    }, 2000);
  }
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('rightCol') rightColRef?: ElementRef<HTMLElement>;
  /** Hauteur de la colonne droite, utilisée pour limiter la gauche */
  rightColHeight: number | null = null;

  ngAfterViewChecked(): void {
    const el = this.rightColRef?.nativeElement;
    if (el && this.spanshRouteAnalysis) {
      const h = el.offsetHeight;
      if (h > 0 && h !== this.rightColHeight) {
        this.rightColHeight = h;
        this.cdr.detectChanges();
      }
    }
  }

  /** Timeout ID du polling en cours (nettoyage) */
  private spanshPollingTimeoutId: ReturnType<typeof setTimeout> | null = null;

  edsnResponse: unknown = null;
  edsnBodiesResponse: unknown = null;
  edsnBodiesEmpty = false;
  edsnBodiesSummary: EdsBodiesSummary | null = null;
  spanshResponse: unknown = null;
  spanshColonisationResponse: unknown = null;
  spanshColonisationResult: unknown = null;
  spanshRouteAnalysis: ColonisationRouteAnalysis | null = null;
  edsnError: string | null = null;
  edsnBodiesError: string | null = null;
  spanshError: string | null = null;
  spanshColonisationError: string | null = null;
  spanshColonisationResultError: string | null = null;
  loadingEdsn = false;
  loadingEdsnBodies = false;
  loadingSpansh = false;
  loadingSpanshColonisation = false;
  loadingSpanshColonisationResult = false;

  systemName = 'Prieluia BP-R d4-105';
  destinationSystem = 'Blae Hypue NH-V e2-2';
  spanshJobId = 'C1E5D9B2-1ACD-11F1-BFBB-B079545E58E4';
  /** Paramètres du dernier job créé (pour affichage du résultat) */
  spanshResultSource: string | null = null;
  spanshResultDestination: string | null = null;
  /** Statut texte du flux Spansh */
  spanshStatusText = '';
  /** Nombre de tentatives de polling */
  spanshPollingAttempt = 0;

  /** Cache centralisé des résumés EDSM (systemName → EdsBodiesSummary | unknown). Réutilisé partout. */
  globalEdsmSummaryCache = new Map<string, EnrichedCandidateEdsm>();
  /** Stats debug de la dernière analyse : totaux, déduplication, appels réels. */
  enrichmentDebugStats: {
    totalCandidates: number;
    uniqueCandidates: number;
    alreadyInCache: number;
    httpCalls: number;
    cacheHits: number;
    cacheMisses: number;
  } | null = null;
  /** Délai entre appels EDSM (ms) pour limiter la charge serveur */
  edsmCallDelayMs = 250;

  /** Enrichissement par PILE : pileIndex → Map<candidateName, EnrichedCandidate> (EDSM, Spansh Lite ou Hybrid) */
  pileEdsmEnrichment = new Map<number, Map<string, EnrichedCandidate>>();
  /** État d'enrichissement par dépôt (not_started | loading | done | no_data | error) */
  pileEnrichmentState = new Map<number, DepositEnrichmentState>();
  /** Fallback ±60 LY déjà utilisé pour ce dépôt (un seul fallback max) */
  pileFallbackUsed = new Map<number, boolean>();
  /** Candidats de la sphère EDSM par dépôt (remplace la fenêtre Spansh quand coords disponibles) */
  sphereCandidatesByPile = new Map<number, Array<{ name: string; distance: number }>>();
  /** Nombre de systèmes trouvés dans la sphère par dépôt (pour affichage) */
  sphereSystemCountByPile = new Map<number, number>();
  /** Index de la PILE en cours d'enrichissement (null = aucune) */
  enrichingPileIndex: number | null = null;
  /** Statut de l'enrichissement automatique (null = inactif) */
  autoEnrichmentStatus: string | null = null;
  /** Résultats du mode comparaison (null si pas en mode comparaison ou pas encore calculé) */
  comparisonResult: { rows: ComparisonRow[]; metrics: ComparisonMetrics } | null = null;

  expandedEdsn = true;

  ngOnDestroy(): void {
    this.stopSpanshPolling();
  }

  private stopSpanshPolling(): void {
    if (this.spanshPollingTimeoutId != null) {
      clearTimeout(this.spanshPollingTimeoutId);
      this.spanshPollingTimeoutId = null;
    }
  }

  private isSpanshResultReady(result: unknown): boolean {
    const data = result as { status?: string; state?: string; result?: { jumps?: unknown[] } };
    return (data?.status === 'ok' || data?.state === 'completed') && Array.isArray(data?.result?.jumps);
  }
  expandedEdsnBodies = true;
  expandedSpansh = true;
  expandedSpanshColonisation = true;
  expandedSpanshColonisationResult = true;
  /** JSON brut Spansh (fermé par défaut, debug) */
  expandedSpanshResultJson = false;
  /** Section debug globale (fermée par défaut) */
  expandedDebug = false;
  /** Sous-sections debug */
  expandedDebugJumps = false;
  expandedDebugCandidates = false;
  expandedDebugJson = false;
  /** Outils API (Job ID, EDSM, Spansh legacy) — fermé par défaut */
  expandedDevtoolsApi = false;

  toggleExpanded(key: 'edsn' | 'edsnBodies' | 'spansh' | 'spanshColonisation' | 'spanshColonisationResult' | 'spanshResultJson' | 'debug' | 'debugJumps' | 'debugCandidates' | 'debugJson' | 'devtoolsApi'): void {
    if (key === 'edsn') this.expandedEdsn = !this.expandedEdsn;
    if (key === 'edsnBodies') this.expandedEdsnBodies = !this.expandedEdsnBodies;
    if (key === 'spansh') this.expandedSpansh = !this.expandedSpansh;
    if (key === 'spanshColonisation') this.expandedSpanshColonisation = !this.expandedSpanshColonisation;
    if (key === 'spanshColonisationResult') this.expandedSpanshColonisationResult = !this.expandedSpanshColonisationResult;
    if (key === 'spanshResultJson') this.expandedSpanshResultJson = !this.expandedSpanshResultJson;
    if (key === 'debug') this.expandedDebug = !this.expandedDebug;
    if (key === 'debugJumps') this.expandedDebugJumps = !this.expandedDebugJumps;
    if (key === 'debugCandidates') this.expandedDebugCandidates = !this.expandedDebugCandidates;
    if (key === 'debugJson') this.expandedDebugJson = !this.expandedDebugJson;
    if (key === 'devtoolsApi') this.expandedDevtoolsApi = !this.expandedDevtoolsApi;
  }

  get spanshResultStatus(): string {
    const d = this.spanshColonisationResult as { status?: string } | null;
    return d?.status ?? '—';
  }

  get spanshResultState(): string {
    const d = this.spanshColonisationResult as { state?: string } | null;
    return d?.state ?? '—';
  }

  testEdsn(): void {
    const name = this.systemName?.trim() || 'Prieluia BP-R d4-105';
    this.loadingEdsn = true;
    this.edsnResponse = null;
    this.edsnError = null;
    this.apiExplorer.testEdsnSystem(name).subscribe({
      next: (data) => {
        this.edsnResponse = data;
        this.loadingEdsn = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.edsnError = err?.message ?? String(err);
        console.error('[ApiExplorerDemo] EDSM error:', err);
        this.loadingEdsn = false;
        this.cdr.detectChanges();
      }
    });
  }

  testEdsnBodies(): void {
    const name = this.systemName?.trim() || 'Prieluia BP-R d4-105';
    this.loadingEdsnBodies = true;
    this.edsnBodiesResponse = null;
    this.edsnBodiesSummary = null;
    this.edsnBodiesEmpty = false;
    this.edsnBodiesError = null;
    this.apiExplorer.testEdsnBodies(name).subscribe({
      next: (data) => {
        this.edsnBodiesResponse = data;
        this.edsnBodiesSummary = this.computeBodiesSummary(data as EdsBodiesData);
        this.edsnBodiesEmpty = this.isBodiesResponseEmpty(data as EdsBodiesData);
        this.loadingEdsnBodies = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.edsnBodiesError = err?.message ?? String(err);
        console.error('[ApiExplorerDemo] EDSM bodies error:', err);
        this.loadingEdsnBodies = false;
        this.cdr.detectChanges();
      }
    });
  }

  private isBodiesResponseEmpty(data: EdsBodiesData | unknown): boolean {
    if (data == null) return true;
    if (Array.isArray(data) && data.length === 0) return true;
    const d = data as EdsBodiesData;
    if (d.bodyCount === 0) return true;
    const bodies = d.bodies ?? [];
    return bodies.length === 0;
  }

  private computeBodiesSummary(data: EdsBodiesData): EdsBodiesSummary {
    const bodies = data?.bodies ?? [];
    const bodyCount = data?.bodyCount ?? bodies.length;

    const rocky = bodies.filter((b) => (b.subType ?? '').includes('Rocky body'));
    const metalRich = bodies.filter((b) => (b.subType ?? '').includes('Metal-rich'));
    const highMetalContent = bodies.filter((b) => (b.subType ?? '').includes('High metal content'));
    const waterWorld = bodies.filter((b) => (b.subType ?? '').includes('Water world'));
    const earthLike = bodies.filter((b) => (b.subType ?? '').includes('Earth-like world'));

    const rockyLandableCount = rocky.filter((b) => b.isLandable === true).length;
    const metalRichLandableCount = metalRich.filter((b) => b.isLandable === true).length;
    const highMetalContentLandableCount = highMetalContent.filter((b) => b.isLandable === true).length;

    return {
      bodyCount,
      landable: bodies.filter((b) => b.isLandable === true).length,
      metalRich: metalRich.length,
      highMetalContent: highMetalContent.length,
      waterWorld: waterWorld.length,
      earthLike: earthLike.length,
      rockyCount: rocky.length,
      rockyLandableCount,
      metalRichCount: metalRich.length,
      metalRichLandableCount,
      highMetalContentCount: highMetalContent.length,
      highMetalContentLandableCount,
      rockyMetalRichLandableCount: metalRichLandableCount
    };
  }

  /**
   * Retourne le libellé métier pour l'affichage (UI uniquement).
   * Ne modifie pas les types internes.
   */
  getDisplayType(type: 'START' | 'TABLIER' | 'PILE' | 'END'): string {
    const map: Record<string, string> = {
      START: 'Départ',
      TABLIER: 'Étape',
      PILE: 'Dépôt',
      END: 'Arrivée'
    };
    return map[type] ?? type;
  }

  /**
   * Libellé métier pour un jump, avec numéro pour les Dépôts (ex: Dépôt #1).
   */
  getDisplayTypeWithIndex(jump: { type: 'START' | 'TABLIER' | 'PILE' | 'END'; pileIndex?: number }): string {
    const label = this.getDisplayType(jump.type);
    return jump.type === 'PILE' && jump.pileIndex != null ? `${label} #${jump.pileIndex}` : label;
  }

  /** Récupère les données enrichies pour un candidat (EDSM, Spansh Lite ou Hybrid) */
  getEdsmEnrichment(pileIndex: number, candidateName: string): EnrichedCandidate | undefined {
    return this.pileEdsmEnrichment.get(pileIndex)?.get(candidateName);
  }

  /** Score métier du candidat (null si non enrichi ou inconnu) */
  getCandidateScore(pileIndex: number, candidate: { name: string; landmark_value?: number }): number | null {
    const analysis = this.getEdsmEnrichment(pileIndex, candidate.name);
    if (!analysis || !isEnrichmentKnown(analysis)) return null;
    return computeScoreFromAnalysis(analysis);
  }

  /** Meilleur score dans la PILE (pour badge BEST), ou null si aucun candidat scoré */
  getBestScoreInPile(pile: { pileIndex: number; candidates: Array<{ name: string; landmark_value?: number }> }): number | null {
    let best: number | null = null;
    for (const c of pile.candidates) {
      const s = this.getCandidateScore(pile.pileIndex, c);
      if (s !== null && (best === null || s > best)) best = s;
    }
    return best;
  }

  /** Taille de fenêtre (± LY) pour la recherche de candidats locaux */
  readonly pileWindowLy = PILE_WINDOW_LY;
  readonly pileWindowFallbackLy = PILE_WINDOW_FALLBACK_LY;

  /** Candidats pour affichage (sphère ou fenêtre) — utilisé par le debug et le scoring */
  getDisplayCandidates(pile: {
    pileIndex: number;
    pileName: string;
    candidates: Array<{ name: string; distance?: number; cumulativeDistance?: number; body_count?: number; landmark_value?: number }>;
  }): Array<{ name: string; distance: number; cumulativeDistance?: number; body_count?: number; landmark_value?: number }> {
    const sphere = this.sphereCandidatesByPile.get(pile.pileIndex);
    if (sphere && sphere.length > 0) {
      return sphere.map((s) => ({
        name: s.name,
        distance: s.distance,
        cumulativeDistance: undefined,
        body_count: undefined,
        landmark_value: undefined
      }));
    }
    return pile.candidates.map((c) => ({
      name: c.name,
      distance: c.distance ?? 0,
      cumulativeDistance: c.cumulativeDistance,
      body_count: c.body_count,
      landmark_value: c.landmark_value
    }));
  }

  /** Candidats effectifs (sphère EDSM si disponible, sinon fenêtre Spansh) */
  private getEffectiveCandidates(pile: {
    pileIndex: number;
    candidates: Array<{ name: string; landmark_value?: number }>;
  }): Array<{ name: string; landmark_value?: number }> {
    const sphere = this.sphereCandidatesByPile.get(pile.pileIndex);
    if (sphere && sphere.length > 0) {
      return sphere.map((s) => ({ name: s.name, landmark_value: undefined }));
    }
    return pile.candidates;
  }

  /**
   * Statistiques de couverture pour le diagnostic d'un dépôt.
   * enrichis = candidats ayant des données EDSM ; edsmUnknown = candidats - enrichis (inconnus ou sans donnée).
   */
  getDepositCoverage(pile: {
    pileIndex: number;
    candidates: Array<{ name: string }>;
  }): { total: number; enriched: number; unknown: number; fromSphere?: boolean } {
    const sphere = this.sphereCandidatesByPile.get(pile.pileIndex);
    const candidates = sphere && sphere.length > 0
      ? sphere.map((s) => ({ name: s.name }))
      : pile.candidates;
    const total = candidates.length;
    const enrichment = this.pileEdsmEnrichment.get(pile.pileIndex);
    if (!enrichment) {
      return { total, enriched: 0, unknown: 0, fromSphere: sphere != null && sphere.length > 0 };
    }
    let enriched = 0;
    for (const c of candidates) {
      const data = enrichment.get(c.name);
      if (data === undefined || !isEnrichmentKnown(data)) continue;
      enriched++;
    }
    const unknown = total - enriched;
    return { total, enriched, unknown, fromSphere: sphere != null && sphere.length > 0 };
  }

  /** État d'enrichissement d'un dépôt (not_started par défaut) */
  getDepositEnrichmentState(pileIndex: number): DepositEnrichmentState {
    return this.pileEnrichmentState.get(pileIndex) ?? 'not_started';
  }

  /** Message fallback pour l'UI (null si pas de fallback utilisé) */
  getDepositFallbackMessage(pileIndex: number): string | null {
    if (!this.pileFallbackUsed.get(pileIndex)) return null;
    const state = this.pileEnrichmentState.get(pileIndex);
    if (state === 'done') return 'Fallback ±60 LY utilisé';
    if (state === 'no_data') return 'Aucune donnée EDSM même après élargissement';
    return null;
  }

  /** Vrai si tous les candidats du dépôt ont été enrichis (EDSMs ou unknown) */
  isDepositFullyEnriched(pile: {
    pileIndex: number;
    candidates: Array<{ name: string }>;
  }): boolean {
    const candidates = this.getEffectiveCandidates(pile);
    if (candidates.length === 0) return true;
    const enrichment = this.pileEdsmEnrichment.get(pile.pileIndex);
    if (!enrichment) return false;
    return candidates.every((c) => enrichment.has(c.name));
  }

  /** Meilleur candidat scoré de la PILE (pour affichage résumé) */
  getBestCandidateInPile(pile: {
    pileIndex: number;
    candidates: Array<{ name: string; landmark_value?: number }>;
  }): { candidate: { name: string; landmark_value?: number }; score: number; edsm: CandidateAnalysisSummary } | null {
    const candidates = this.getEffectiveCandidates(pile);
    let best: { candidate: { name: string; landmark_value?: number }; score: number; edsm: CandidateAnalysisSummary } | null = null;
    for (const c of candidates) {
      const analysis = this.getEdsmEnrichment(pile.pileIndex, c.name);
      if (!analysis || !isEnrichmentKnown(analysis)) continue;
      const score = computeScoreFromAnalysis(analysis);
      if (best === null || score > best.score) {
        best = { candidate: c, score, edsm: analysis };
      }
    }
    return best;
  }

  /** Capture le meilleur candidat par dépôt (pour le mode comparaison) */
  private captureBestPerPile(): Map<number, { name: string; score: number } | null> {
    const out = new Map<number, { name: string; score: number } | null>();
    const piles = this.spanshRouteAnalysis?.localCandidatesByPile ?? [];
    for (const pile of piles) {
      const best = this.getBestCandidateInPile(pile);
      out.set(pile.pileIndex, best ? { name: best.candidate.name, score: best.score } : null);
    }
    return out;
  }

  /** Réinitialise l'état des dépôts pour une nouvelle run (conserve le cache EDSM global) */
  private clearPileStateForRun(): void {
    this.pileEdsmEnrichment.clear();
    this.pileEnrichmentState.clear();
    this.pileFallbackUsed.clear();
    this.sphereCandidatesByPile.clear();
    this.sphereSystemCountByPile.clear();
  }

  /** Récupère ou calcule le résumé EDSM pour un système (utilise le cache centralisé) */
  private getOrFetchEdsmSummary(
    systemName: string
  ): Observable<EnrichedCandidateEdsm> {
    const key = systemName?.trim() ?? '';
    const cached = this.globalEdsmSummaryCache.get(key);
    if (cached !== undefined) {
      return of(cached);
    }
    return this.apiExplorer.testEdsnBodies(systemName).pipe(
      map((data) => {
        const isEmpty = this.isBodiesResponseEmpty(data as EdsBodiesData);
        const enriched: EnrichedCandidateEdsm = isEmpty ? { unknown: true } : this.computeBodiesSummary(data as EdsBodiesData);
        this.globalEdsmSummaryCache.set(key, enriched);
        if (this.enrichmentDebugStats) {
          this.enrichmentDebugStats.httpCalls++;
          this.enrichmentDebugStats.cacheHits = this.apiExplorer.edsmCacheStats.hits;
          this.enrichmentDebugStats.cacheMisses = this.apiExplorer.edsmCacheStats.misses;
        }
        return enriched;
      }),
      catchError(() => {
        const fallback: EnrichedCandidateEdsm = { unknown: true };
        this.globalEdsmSummaryCache.set(key, fallback);
        if (this.enrichmentDebugStats) this.enrichmentDebugStats.httpCalls++;
        return of(fallback);
      })
    );
  }

  /**
   * Effectue une tentative d'enrichissement (fenêtre normale ou fallback ±60 LY).
   * Utilise le cache centralisé pour éviter les appels redondants.
   */
  private enrichSingleDepositAttempt(pileIndex: number, useFallbackWindow: boolean): Observable<void> {
    const pile = this.spanshRouteAnalysis?.localCandidatesByPile.find((p) => p.pileIndex === pileIndex);
    if (!pile) return of(undefined);

    if (!this.pileEdsmEnrichment.has(pileIndex)) {
      this.pileEdsmEnrichment.set(pileIndex, new Map());
    }
    const results = this.pileEdsmEnrichment.get(pileIndex)!;
    if (useFallbackWindow) results.clear();

    // Mode Spansh Lite : pas d'appel EDSM
    if (this.enrichmentMode === 'spansh-lite') {
      const getCandidates = (): LocalPileCandidate[] => {
        if (useFallbackWindow) {
          const data = this.spanshColonisationResult as { result?: { jumps?: SpanshJump[] } } | null;
          const jumps = data?.result?.jumps;
          if (!Array.isArray(jumps)) return pile.candidates;
          return getCandidatesInWindow(jumps, pile.pileCumulativeDistance, PILE_WINDOW_FALLBACK_LY);
        }
        return pile.candidates;
      };
      for (const c of getCandidates()) {
        results.set(c.name, buildSpanshLiteSummary(c));
      }
      this.pileEnrichmentState.set(pileIndex, 'done');
      this.sphereCandidatesByPile.delete(pileIndex);
      this.sphereSystemCountByPile.set(pileIndex, 0);
      this.cdr.detectChanges();
      return of(undefined);
    }

    const hasCoords =
      typeof pile.pileX === 'number' && typeof pile.pileY === 'number' && typeof pile.pileZ === 'number';
    const sphereRadius = useFallbackWindow ? 60 : 50;

    const getPileCandidates = (): Array<{ name: string }> => {
      if (useFallbackWindow) {
        const data = this.spanshColonisationResult as { result?: { jumps?: SpanshJump[] } } | null;
        const jumps = data?.result?.jumps;
        if (!Array.isArray(jumps)) return pile.candidates;
        return getCandidatesInWindow(jumps, pile.pileCumulativeDistance, PILE_WINDOW_FALLBACK_LY);
      }
      return pile.candidates;
    };

    const sphere$ = hasCoords
      ? this.apiExplorer.getEdsmSphereSystemsByCoords(pile.pileX!, pile.pileY!, pile.pileZ!, sphereRadius)
      : this.apiExplorer.getEdsmSphereSystemsByName(pile.pileName, sphereRadius);

    return sphere$.pipe(
      map((systems) => ({ ok: true as const, systems })),
      catchError(() => of({ ok: false as const, systems: null })),
      concatMap(({ ok, systems }) => {
        const list = ok && systems?.length ? systems : getPileCandidates();
        if (!list?.length) {
          this.sphereSystemCountByPile.set(pileIndex, 0);
          this.pileEnrichmentState.set(pileIndex, 'no_data');
          this.cdr.detectChanges();
          return of(undefined);
        }
        if (ok && systems?.length) {
          this.sphereCandidatesByPile.set(pileIndex, systems.map((s) => ({ name: s.name, distance: s.distance })));
          this.sphereSystemCountByPile.set(pileIndex, systems.length);
        } else {
          this.sphereSystemCountByPile.set(pileIndex, 0);
        }
        this.pileEnrichmentState.set(pileIndex, 'loading');
        this.cdr.detectChanges();
        return from(list).pipe(
          concatMap((item) =>
            this.getOrFetchEdsmSummary(item.name).pipe(
              map((raw) => {
                const enriched: EnrichedCandidate =
                  'unknown' in raw && raw.unknown
                    ? raw
                    : edsBodiesToAnalysisSummary(raw as EdsBodiesSummary, (item as LocalPileCandidate).landmark_value ?? 0);
                results.set(item.name, enriched);
                this.cdr.detectChanges();
              }),
              delay(this.edsmCallDelayMs)
            )
          ),
          finalize(() => {
            const cov = this.getDepositCoverage(pile);
            this.pileEnrichmentState.set(pileIndex, cov.enriched > 0 ? 'done' : 'no_data');
            this.cdr.detectChanges();
          })
        );
      }),
      catchError(() => {
        this.pileEnrichmentState.set(pileIndex, 'error');
        this.cdr.detectChanges();
        return of(undefined);
      })
    );
  }

  /** Enrichit un seul dépôt. Si no_data après 1re tentative, fallback auto ±60 LY (une seule fois). */
  private enrichSingleDeposit(pileIndex: number): Observable<void> {
    const pile = this.spanshRouteAnalysis?.localCandidatesByPile.find((p) => p.pileIndex === pileIndex);
    if (!pile) return of(undefined);
    return this.enrichSingleDepositAttempt(pileIndex, false).pipe(
      concatMap(() => {
        if (this.pileEnrichmentState.get(pileIndex) === 'no_data' && !this.pileFallbackUsed.get(pileIndex)) {
          this.pileFallbackUsed.set(pileIndex, true);
          return this.enrichSingleDepositAttempt(pileIndex, true).pipe(delay(250));
        }
        return of(undefined);
      })
    );
  }

  /** Lance l'enrichissement EDSM pour un dépôt (manuel). Ne lance pas si le dépôt est déjà en loading. */
  enrichPileWithEdsm(pileIndex: number): void {
    const pile = this.spanshRouteAnalysis?.localCandidatesByPile.find((p) => p.pileIndex === pileIndex);
    if (!pile) return;
    const hasCoords =
      typeof pile.pileX === 'number' && typeof pile.pileY === 'number' && typeof pile.pileZ === 'number';
    if (!hasCoords && pile.candidates.length === 0) return;
    if (this.enrichingPileIndex != null) return;
    if (this.getDepositEnrichmentState(pileIndex) === 'loading') return;

    this.pileFallbackUsed.set(pileIndex, false);
    this.enrichingPileIndex = pileIndex;
    this.enrichSingleDeposit(pileIndex)
      .pipe(
        finalize(() => {
          this.enrichingPileIndex = null;
          this.cdr.detectChanges();
        })
      )
      .subscribe();
  }

  /**
   * Lance l'enrichissement selon le mode.
   * En mode comparison, exécute les 3 analyses et affiche le tableau de comparaison.
   */
  private enrichAllDepositsAutomatically(): void {
    if (this.enrichmentMode === 'comparison') {
      this.runComparisonMode();
      return;
    }
    this.enrichAllDepositsForMode(this.enrichmentMode as 'edsm' | 'spansh-lite' | 'hybrid').subscribe();
  }

  /**
   * Lance l'enrichissement pour un mode donné. Retourne un Observable qui se complète à la fin.
   */
  private enrichAllDepositsForMode(mode: 'edsm' | 'spansh-lite' | 'hybrid'): Observable<void> {
    const allPiles = this.spanshRouteAnalysis?.localCandidatesByPile ?? [];
    const piles = allPiles.filter((p) => {
      const s = this.getDepositEnrichmentState(p.pileIndex);
      return s === 'not_started' || s === 'error';
    });
    if (piles.length === 0) {
      console.warn('[comparison] enrichAllDepositsForMode: no piles to process (filtered)', { total: allPiles.length });
      return of(undefined);
    }
    if (this.enrichingPileIndex != null) {
      console.warn('[comparison] enrichAllDepositsForMode: already enriching, skip', { mode });
      return of(undefined);
    }

    this.enrichingPileIndex = -1;
    this.apiExplorer.enrichmentMode = mode;
    this.cdr.detectChanges();
    this.enrichmentDebugStats = {
      totalCandidates: 0,
      uniqueCandidates: 0,
      alreadyInCache: 0,
      httpCalls: 0,
      cacheHits: 0,
      cacheMisses: 0
    };

    // Mode Spansh Lite : pas d'appel EDSM, remplissage immédiat depuis les données Spansh
    if (mode === 'spansh-lite') {
      return of(undefined).pipe(
        tap(() => {
          for (const pile of piles) {
            if (!this.pileEdsmEnrichment.has(pile.pileIndex)) {
              this.pileEdsmEnrichment.set(pile.pileIndex, new Map());
            }
            const results = this.pileEdsmEnrichment.get(pile.pileIndex)!;
            for (const c of pile.candidates) {
              results.set(c.name, buildSpanshLiteSummary(c));
            }
            this.pileEnrichmentState.set(pile.pileIndex, 'done');
            this.sphereCandidatesByPile.delete(pile.pileIndex);
            this.sphereSystemCountByPile.set(pile.pileIndex, 0);
          }
          this.enrichingPileIndex = null;
          this.autoEnrichmentStatus = 'Analyse Spansh Lite terminée';
        }),
        tap(() => this.cdr.detectChanges())
      );
    }

    interface PileCandidates { pileIndex: number; pile: typeof allPiles[0]; candidates: LocalPileCandidate[]; fromSphere: boolean; systems?: { name: string; distance: number }[]; }

    const getCandidatesForPile = (pile: typeof allPiles[0]): Observable<PileCandidates> => {
      // Mode hybrid : uniquement pile.candidates (données Spansh pour pre-score)
      if (mode === 'hybrid') {
        return of({
          pileIndex: pile.pileIndex,
          pile,
          candidates: pile.candidates,
          fromSphere: false
        });
      }
      const hasCoords = typeof pile.pileX === 'number' && typeof pile.pileY === 'number' && typeof pile.pileZ === 'number';
      if (!hasCoords) {
        return of({
          pileIndex: pile.pileIndex,
          pile,
          candidates: pile.candidates,
          fromSphere: false
        });
      }
      const sphere$ = this.apiExplorer.getEdsmSphereSystemsByCoords(pile.pileX!, pile.pileY!, pile.pileZ!, 50);
      return sphere$.pipe(
        map((systems) => ({
          pileIndex: pile.pileIndex,
          pile,
          candidates: systems?.map((s) => ({ name: s.name, distance: s.distance, cumulativeDistance: 0 })) ?? pile.candidates,
          fromSphere: !!(systems?.length),
          systems
        })),
        catchError(() => of({
          pileIndex: pile.pileIndex,
          pile,
          candidates: pile.candidates,
          fromSphere: false
        }))
      );
    };

    return from(piles)
      .pipe(
        concatMap((p) => getCandidatesForPile(p).pipe(delay(100))),
        toArray(),
        concatMap((pileDataList: PileCandidates[]) => {
          let totalCandidates = 0;
          const allNames = new Set<string>();
          const pileToCandidates = new Map<number, PileCandidates>();
          for (const pd of pileDataList) {
            pileToCandidates.set(pd.pileIndex, pd);
            for (const c of pd.candidates) {
              allNames.add(c.name);
              totalCandidates++;
            }
          }
          const uniqueCandidates = allNames.size;

          // Mode hybrid : ne garder que les top N par dépôt (pre-score Spansh)
          let toEnrich: string[];
          const hybridTopNames = new Set<string>();
          if (mode === 'hybrid') {
            const topN = Math.max(1, this.hybridTopN);
            for (const pd of pileDataList) {
              const withPreScore = pd.candidates
                .map((c) => {
                  const summary = buildSpanshLiteSummary(c);
                  return { c, preScore: computeSpanshPreScore(summary) };
                })
                .sort((a, b) => b.preScore - a.preScore)
                .slice(0, topN);
              for (const { c } of withPreScore) hybridTopNames.add(c.name);
            }
            toEnrich = [...hybridTopNames].filter((n) => !this.globalEdsmSummaryCache.has(n));
          } else {
            toEnrich = [...allNames].filter((n) => !this.globalEdsmSummaryCache.has(n));
          }
          const alreadyInCache = (mode === 'hybrid' ? hybridTopNames.size : uniqueCandidates) - toEnrich.length;

          this.enrichmentDebugStats!.totalCandidates = totalCandidates;
          this.enrichmentDebugStats!.uniqueCandidates = uniqueCandidates;
          this.enrichmentDebugStats!.alreadyInCache = alreadyInCache;

          const modeLabel = mode === 'hybrid' ? 'Hybrid (top ' + this.hybridTopN + ')' : 'EDSM';
          this.autoEnrichmentStatus = `Enrichissement ${modeLabel} : ${toEnrich.length} systèmes à appeler (${alreadyInCache} en cache)…`;
          this.cdr.detectChanges();

          const enrich$ = toEnrich.length === 0
            ? of([])
            : from(toEnrich).pipe(
                concatMap((systemName) =>
                  this.getOrFetchEdsmSummary(systemName).pipe(
                    delay(this.edsmCallDelayMs),
                    map(() => undefined)
                  )
                ),
                toArray()
              );

          return enrich$.pipe(
            map(() => {
              for (const pd of pileDataList) {
                if (!this.pileEdsmEnrichment.has(pd.pileIndex)) {
                  this.pileEdsmEnrichment.set(pd.pileIndex, new Map());
                }
                const results = this.pileEdsmEnrichment.get(pd.pileIndex)!;
                if (pd.fromSphere && pd.systems?.length) {
                  this.sphereCandidatesByPile.set(pd.pileIndex, pd.systems);
                  this.sphereSystemCountByPile.set(pd.pileIndex, pd.systems.length);
                } else {
                  this.sphereSystemCountByPile.set(pd.pileIndex, 0);
                }
                for (const c of pd.candidates) {
                  const raw = this.globalEdsmSummaryCache.get(c.name);
                  let enriched: EnrichedCandidate;
                  if (raw !== undefined) {
                    enriched =
                      'unknown' in raw && raw.unknown
                        ? raw
                        : edsBodiesToAnalysisSummary(raw as EdsBodiesSummary, c.landmark_value ?? 0);
                  } else if (mode === 'hybrid') {
                    enriched = buildSpanshLiteSummary(c);
                  } else {
                    continue;
                  }
                  results.set(c.name, enriched);
                }
                const cov = this.getDepositCoverage(pd.pile);
                this.pileEnrichmentState.set(pd.pileIndex, cov.enriched > 0 ? 'done' : 'no_data');
              }
            }),
            map(() => undefined)
          );
        }),
        concatMap(() => this.runFallbackForNoDataPiles(piles)),
        finalize(() => {
          this.enrichingPileIndex = null;
          if (this.enrichmentDebugStats) {
            this.enrichmentDebugStats.cacheHits = this.apiExplorer.edsmCacheStats.hits;
            this.enrichmentDebugStats.cacheMisses = this.apiExplorer.edsmCacheStats.misses;
          }
          const allP = this.spanshRouteAnalysis?.localCandidatesByPile ?? [];
          const totalEnriched = allP.reduce((s, p) => s + this.getDepositCoverage(p).enriched, 0);
          const modeLabel = mode === 'hybrid' ? 'Hybrid' : 'EDSM';
          this.autoEnrichmentStatus = totalEnriched > 0
            ? `Analyse ${modeLabel} terminée`
            : `Analyse ${modeLabel} terminée (aucune donnée trouvée)`;
          this.cdr.detectChanges();
        })
      );
  }

  /** Exécute les 3 modes et construit le tableau de comparaison */
  private runComparisonMode(): void {
    const allPiles = this.spanshRouteAnalysis?.localCandidatesByPile ?? [];
    console.log('[comparison] start');
    console.log('[comparison] deposits detected:', allPiles.length);
    if (allPiles.length === 0) return;
    if (this.enrichingPileIndex != null) {
      console.warn('[comparison] blocked: enrichment already in progress');
      return;
    }

    this.comparisonResult = null;
    this.autoEnrichmentStatus = 'Comparaison : analyse EDSM…';
    this.cdr.detectChanges();

    const t0 = Date.now();
    const edsmCallsBefore = this.apiExplorer.edsmCacheStats.misses;

    const modes: Array<'edsm' | 'spansh-lite' | 'hybrid'> = ['edsm', 'spansh-lite', 'hybrid'];
    for (const p of allPiles) {
      console.log(`[comparison] deposit ${p.pileIndex} candidates:`, p.candidates?.length ?? 0);
    }
    from(modes)
      .pipe(
        concatMap((m, idx) => {
          const labels = ['EDSM', 'Spansh Lite', 'Hybrid'];
          console.log('[comparison] running mode', labels[idx]);
          if (idx > 0) {
            this.clearPileStateForRun();
            this.autoEnrichmentStatus = `Comparaison : analyse ${labels[idx]}…`;
            this.cdr.detectChanges();
          }
          return this.enrichAllDepositsForMode(m).pipe(
            map(() => ({ mode: m, results: this.captureBestPerPile() }))
          );
        }),
        toArray(),
        finalize(() => {
          this.enrichingPileIndex = null;
          this.cdr.detectChanges();
        })
      )
      .subscribe({
        next: (allResults) => {
          const [edsmRes, spanshRes, hybridRes] = allResults;
          const rows: ComparisonRow[] = [];
          const uniqueSystems = new Set<string>();
          let noDataCount = 0;

          for (const pile of allPiles) {
            const edsm = edsmRes.results.get(pile.pileIndex) ?? null;
            const spanshLite = spanshRes.results.get(pile.pileIndex) ?? null;
            const hybrid = hybridRes.results.get(pile.pileIndex) ?? null;
            if (edsm) uniqueSystems.add(edsm.name);
            if (spanshLite) uniqueSystems.add(spanshLite.name);
            if (hybrid) uniqueSystems.add(hybrid.name);
            if (!edsm && !spanshLite && !hybrid) noDataCount++;

            const bestNames = [edsm?.name, spanshLite?.name, hybrid?.name].filter(Boolean) as string[];
            const hasDifferences = new Set(bestNames).size > 1;

            rows.push({
              pileIndex: pile.pileIndex,
              edsm,
              spanshLite,
              hybrid,
              hasDifferences
            });
          }

          const totalTimeMs = Date.now() - t0;
          const edsmCalls = this.apiExplorer.edsmCacheStats.misses - edsmCallsBefore;

          this.comparisonResult = {
            rows,
            metrics: {
              totalTimeMs,
              edsmCalls,
              enrichedCount: uniqueSystems.size,
              noDataCount
            }
          };
          this.autoEnrichmentStatus = 'Comparaison terminée';
          this.cdr.detectChanges();
        },
        error: () => {
          this.autoEnrichmentStatus = 'Erreur lors de la comparaison';
          this.cdr.detectChanges();
        }
      });
  }

  /** Fallback ±60 LY pour les dépôts en no_data (une seule fois). */
  private runFallbackForNoDataPiles(piles: Array<{ pileIndex: number }>): Observable<void> {
    const noDataPiles = piles.filter((p) => this.pileEnrichmentState.get(p.pileIndex) === 'no_data' && !this.pileFallbackUsed.get(p.pileIndex));
    if (noDataPiles.length === 0) return of(undefined);

    const data = this.spanshColonisationResult as { result?: { jumps?: SpanshJump[] } } | null;
    const jumps = data?.result?.jumps;
    if (!Array.isArray(jumps)) return of(undefined);

    const allNames = new Set<string>();
    const pileToWiderCandidates = new Map<number, LocalPileCandidate[]>();
    for (const p of noDataPiles) {
      const pile = this.spanshRouteAnalysis?.localCandidatesByPile.find((x) => x.pileIndex === p.pileIndex);
      if (!pile) continue;
      const wider = getCandidatesInWindow(jumps, pile.pileCumulativeDistance, PILE_WINDOW_FALLBACK_LY);
      pileToWiderCandidates.set(p.pileIndex, wider);
      for (const c of wider) allNames.add(c.name);
    }
    const toEnrich = [...allNames].filter((n) => !this.globalEdsmSummaryCache.has(n));
    if (toEnrich.length === 0) return of(undefined);

    this.autoEnrichmentStatus = `Fallback ±60 LY : ${toEnrich.length} systèmes à enrichir…`;
    this.cdr.detectChanges();

    return from(toEnrich).pipe(
      concatMap((systemName) =>
        this.getOrFetchEdsmSummary(systemName).pipe(
          delay(this.edsmCallDelayMs),
          map(() => undefined)
        )
      ),
      map(() => {
        for (const [pileIndex, candidates] of pileToWiderCandidates) {
          this.pileFallbackUsed.set(pileIndex, true);
          if (!this.pileEdsmEnrichment.has(pileIndex)) this.pileEdsmEnrichment.set(pileIndex, new Map());
          const results = this.pileEdsmEnrichment.get(pileIndex)!;
          results.clear();
          this.sphereCandidatesByPile.set(pileIndex, candidates);
          this.sphereSystemCountByPile.set(pileIndex, candidates.length);
          for (const c of candidates) {
            const raw = this.globalEdsmSummaryCache.get(c.name);
            if (raw === undefined) continue;
            const enriched: EnrichedCandidate =
              'unknown' in raw && raw.unknown
                ? raw
                : edsBodiesToAnalysisSummary(raw as EdsBodiesSummary, c.landmark_value ?? 0);
            results.set(c.name, enriched);
          }
          const pile = this.spanshRouteAnalysis?.localCandidatesByPile.find((x) => x.pileIndex === pileIndex);
          if (pile) {
            const cov = this.getDepositCoverage(pile);
            this.pileEnrichmentState.set(pileIndex, cov.enriched > 0 ? 'done' : 'no_data');
          }
        }
        this.cdr.detectChanges();
      }),
      map(() => undefined)
    );
  }

  testSpanshColonisationRoute(): void {
    const source = this.systemName?.trim() || 'Prieluia BP-R d4-105';
    const destination = this.destinationSystem?.trim() || 'Blae Hypue NH-V e2-2';
    this.stopSpanshPolling();
    this.loadingSpanshColonisation = true;
    this.loadingSpanshColonisationResult = true;
    this.spanshColonisationResponse = null;
    this.spanshColonisationResult = null;
    this.spanshRouteAnalysis = null;
    this.spanshColonisationError = null;
    this.spanshColonisationResultError = null;
    this.spanshPollingAttempt = 0;
    this.spanshStatusText = 'Création du job...';
    this.cdr.detectChanges();

    this.apiExplorer.testSpanshColonisationRoute(source, destination).subscribe({
      next: (data) => {
        this.spanshColonisationResponse = data;
        this.loadingSpanshColonisation = false;
        this.spanshResultSource = source;
        this.spanshResultDestination = destination;
        const jobId = (data as { job?: string })?.job;
        if (jobId) {
          this.spanshJobId = jobId;
          this.spanshStatusText = 'Job créé, attente du résultat...';
          this.cdr.detectChanges();
          this.scheduleSpanshPoll();
        } else {
          this.loadingSpanshColonisationResult = false;
          this.spanshStatusText = 'Aucun job ID reçu.';
          this.cdr.detectChanges();
        }
      },
      error: (err) => {
        this.spanshColonisationError = err?.message ?? String(err);
        console.error('[ApiExplorerDemo] Spansh Colonisation error:', err);
        this.loadingSpanshColonisation = false;
        this.loadingSpanshColonisationResult = false;
        this.spanshStatusText = 'Erreur lors de la création du job.';
        this.cdr.detectChanges();
      }
    });
  }

  private scheduleSpanshPoll(): void {
    this.spanshPollingTimeoutId = setTimeout(() => this.pollSpanshResult(), 2000);
  }

  private pollSpanshResult(): void {
    this.spanshPollingTimeoutId = null;
    this.spanshPollingAttempt++;
    this.spanshStatusText = `Calcul en cours... (tentative ${this.spanshPollingAttempt})`;
    this.cdr.detectChanges();

    this.apiExplorer.getSpanshColonisationResult(this.spanshJobId).subscribe({
      next: (result) => {
        if (this.isSpanshResultReady(result)) {
          this.spanshColonisationResult = result;
          this.spanshRouteAnalysis = this.computeRouteAnalysis(result);
          this.pileEdsmEnrichment.clear();
          this.pileEnrichmentState.clear();
          this.pileFallbackUsed.clear();
          this.sphereCandidatesByPile.clear();
          this.sphereSystemCountByPile.clear();
          this.comparisonResult = null;
          this.autoEnrichmentStatus = null;
          this.loadingSpanshColonisationResult = false;
          this.spanshStatusText =
            this.spanshPollingAttempt > 1
              ? `Résultat récupéré (après ${this.spanshPollingAttempt} tentatives).`
              : 'Résultat récupéré.';
          this.cdr.detectChanges();
          this.enrichAllDepositsAutomatically();
        } else {
          this.scheduleSpanshPoll();
        }
      },
      error: (err) => {
        this.spanshColonisationResultError = err?.message ?? String(err);
        console.error('[ApiExplorerDemo] Spansh Colonisation result error:', err);
        this.loadingSpanshColonisationResult = false;
        this.spanshStatusText = 'Erreur lors de la récupération du résultat.';
        this.cdr.detectChanges();
      }
    });
  }

  fetchSpanshColonisationResult(): void {
    const jobId = this.spanshJobId?.trim();
    if (!jobId) return;
    this.stopSpanshPolling();
    this.loadingSpanshColonisationResult = true;
    this.spanshColonisationResult = null;
    this.spanshRouteAnalysis = null;
    this.spanshColonisationResultError = null;
    this.spanshStatusText = 'Récupération manuelle en cours...';
    this.spanshResultSource = this.spanshResultSource ?? (this.systemName?.trim() || null);
    this.spanshResultDestination = this.spanshResultDestination ?? (this.destinationSystem?.trim() || null);
    this.cdr.detectChanges();
    this.apiExplorer.getSpanshColonisationResult(jobId).subscribe({
      next: (result) => {
        this.spanshColonisationResult = result;
        this.spanshRouteAnalysis = this.computeRouteAnalysis(result);
        this.pileEdsmEnrichment.clear();
        this.pileEnrichmentState.clear();
        this.pileFallbackUsed.clear();
        this.sphereCandidatesByPile.clear();
        this.sphereSystemCountByPile.clear();
        this.comparisonResult = null;
        this.autoEnrichmentStatus = null;
        this.loadingSpanshColonisationResult = false;
        this.spanshStatusText = 'Résultat récupéré.';
        this.cdr.detectChanges();
        this.enrichAllDepositsAutomatically();
      },
      error: (err) => {
        this.spanshColonisationResultError = err?.message ?? String(err);
        console.error('[ApiExplorerDemo] Spansh Colonisation result error:', err);
        this.loadingSpanshColonisationResult = false;
        this.spanshStatusText = 'Erreur lors de la récupération du résultat.';
        this.cdr.detectChanges();
      }
    });
  }

  private computeRouteAnalysis(result: unknown): ColonisationRouteAnalysis | null {
    const data = result as { result?: { jumps?: SpanshJump[] } };
    const jumps = data?.result?.jumps;
    if (!Array.isArray(jumps)) return null;
    return analyzeColonisationRoute(jumps);
  }

  testSpansh(): void {
    this.loadingSpansh = true;
    this.spanshResponse = null;
    this.spanshError = null;
    this.apiExplorer.testSpanshRoute('Sol').subscribe({
      next: (data) => {
        this.spanshResponse = data;
        this.loadingSpansh = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.spanshError = err?.message ?? String(err);
        console.error('[ApiExplorerDemo] Spansh error:', err);
        this.loadingSpansh = false;
        this.cdr.detectChanges();
      }
    });
  }
}
