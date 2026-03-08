/**
 * Types communs pour l'enrichissement des candidats PILE.
 * Permet plusieurs sources : EDSM, Spansh Lite, Hybrid.
 */

/** Mode d'enrichissement : EDSM complet, Spansh seul, ou hybride (pre-score Spansh + EDSM top N) */
export type EnrichmentMode = 'edsm' | 'spansh-lite' | 'hybrid';

/**
 * Interface commune pour les données d'analyse d'un candidat.
 * Utilisée par EDSM, Spansh Lite et Hybrid.
 */
export interface CandidateAnalysisSummary {
  source: 'edsm' | 'spansh-lite';
  bodyCount: number;
  /** Pour compatibilité affichage debug */
  rockyCount?: number;
  metalRichCount?: number;
  highMetalContentCount?: number;
  metalRichLandableCount: number;
  rockyLandableCount: number;
  highMetalContentLandableCount: number;
  waterWorldCount: number;
  earthLikeWorldCount: number;
  landmarkValue: number;
  estimatedScanValue?: number;
  estimatedMappingValue?: number;
  unknown?: boolean;
}

/** Résultat d'enrichissement : données ou inconnu */
export type EnrichedCandidate = CandidateAnalysisSummary | { unknown: true };

/** Vérifie si l'enrichissement contient des données valides (pas unknown) */
export function isEnrichmentKnown(e: EnrichedCandidate): e is CandidateAnalysisSummary {
  return e != null && !('unknown' in e && e.unknown === true);
}

/** Candidat local Spansh (données de la route) */
export interface SpanshCandidateLike {
  name: string;
  body_count?: number;
  landmark_value?: number;
  estimated_scan_value?: number;
  estimated_mapping_value?: number;
}

/**
 * Construit un CandidateAnalysisSummary à partir des données Spansh uniquement.
 * Pas d'appel EDSM. Métriques indisponibles = 0.
 */
export function buildSpanshLiteSummary(candidate: SpanshCandidateLike): CandidateAnalysisSummary {
  const bodyCount = typeof candidate.body_count === 'number' && !Number.isNaN(candidate.body_count) ? candidate.body_count : 0;
  const landmarkValue = typeof candidate.landmark_value === 'number' && !Number.isNaN(candidate.landmark_value) ? candidate.landmark_value : 0;
  return {
    source: 'spansh-lite',
    bodyCount,
    rockyCount: 0,
    metalRichCount: 0,
    highMetalContentCount: 0,
    metalRichLandableCount: 0,
    rockyLandableCount: 0,
    highMetalContentLandableCount: 0,
    waterWorldCount: 0,
    earthLikeWorldCount: 0,
    landmarkValue,
    estimatedScanValue: candidate.estimated_scan_value,
    estimatedMappingValue: candidate.estimated_mapping_value
  };
}

/**
 * Pre-score pour le mode hybrid : utilise bodyCount, landmarkValue, estimatedScan, estimatedMapping.
 * Permet de trier les candidats avant d'appeler EDSM sur les top N.
 */
export function computeSpanshPreScore(a: CandidateAnalysisSummary): number {
  const body = a.bodyCount ?? 0;
  const lv = a.landmarkValue ?? 0;
  const scan = a.estimatedScanValue ?? 0;
  const mapVal = a.estimatedMappingValue ?? 0;
  return body * 8 + lv * 0.01 + scan * 0.001 + mapVal * 0.001;
}

/**
 * Score PILE final (formule EDSM inchangée).
 * Fonctionne avec CandidateAnalysisSummary (EDSM ou Spansh Lite).
 */
export function computeScoreFromAnalysis(a: CandidateAnalysisSummary): number {
  const lv = a.landmarkValue ?? 0;
  return (
    (a.metalRichLandableCount ?? 0) * 100 +
    (a.rockyLandableCount ?? 0) * 40 +
    (a.highMetalContentLandableCount ?? 0) * 25 +
    (a.bodyCount ?? 0) * 8 +
    (a.waterWorldCount ?? 0) * 20 +
    (a.earthLikeWorldCount ?? 0) * 15 +
    lv * 0.01
  );
}

/** Structure EDSM bodies (pour conversion) */
export interface EdsBodiesSummaryLike {
  bodyCount: number;
  waterWorld: number;
  earthLike: number;
  rockyLandableCount: number;
  metalRichLandableCount: number;
  highMetalContentLandableCount: number;
}

/**
 * Convertit un résumé EDSM en CandidateAnalysisSummary.
 */
/** Structure EDSM complète pour conversion */
export interface EdsBodiesSummaryFull extends EdsBodiesSummaryLike {
  rockyCount?: number;
  metalRichCount?: number;
  highMetalContentCount?: number;
}

export function edsBodiesToAnalysisSummary(
  edsm: EdsBodiesSummaryFull,
  landmarkValue: number
): CandidateAnalysisSummary {
  return {
    source: 'edsm',
    bodyCount: edsm.bodyCount ?? 0,
    rockyCount: edsm.rockyCount ?? 0,
    metalRichCount: edsm.metalRichCount ?? 0,
    highMetalContentCount: edsm.highMetalContentCount ?? 0,
    metalRichLandableCount: edsm.metalRichLandableCount ?? 0,
    rockyLandableCount: edsm.rockyLandableCount ?? 0,
    highMetalContentLandableCount: edsm.highMetalContentLandableCount ?? 0,
    waterWorldCount: edsm.waterWorld ?? 0,
    earthLikeWorldCount: edsm.earthLike ?? 0,
    landmarkValue
  };
}
