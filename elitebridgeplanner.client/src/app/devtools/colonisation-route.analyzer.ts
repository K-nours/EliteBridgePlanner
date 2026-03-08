/**
 * Analyse une route de colonisation Spansh pour extraire des métriques de planification de pont.
 * Ne contient pas de logique de scoring.
 */

export interface SpanshJump {
  name: string;
  distance: number;
  distance_to_destination?: number;
  x?: number;
  y?: number;
  z?: number;
  body_count?: number;
  landmark_value?: number;
  estimated_scan_value?: number;
  estimated_mapping_value?: number;
}

export interface AnalyzedJump {
  index: number;
  name: string;
  distance: number;
  cumulativeDistance: number;
  /** Distance depuis le dernier ancrage (pour validation PILE 430–499 LY) */
  distanceFromLastAnchor: number;
  /** Index du PILE (1, 2, 3...) — uniquement pour type PILE */
  pileIndex?: number;
  type: 'START' | 'TABLIER' | 'PILE' | 'END';
}

export interface LocalPileCandidate {
  name: string;
  distance: number;
  cumulativeDistance: number;
  body_count?: number;
  landmark_value?: number;
  estimated_scan_value?: number;
  estimated_mapping_value?: number;
}

export interface PileLocalCandidates {
  pileIndex: number;
  pileName: string;
  pileCumulativeDistance: number;
  /** Coordonnées du dépôt (pour EDSM sphere-systems) */
  pileX?: number;
  pileY?: number;
  pileZ?: number;
  candidates: LocalPileCandidate[];
}

export interface ColonisationRouteAnalysis {
  jumps: AnalyzedJump[];
  summary: {
    totalJumps: number;
    totalDistance: number;
    pileCandidates: number;
  };
  /** Candidats locaux autour de chaque PILE (± PILE_WINDOW_LY) */
  localCandidatesByPile: PileLocalCandidates[];
}

const PILE_MIN_LY = 430;
const PILE_MAX_LY = 499;
/** Fenêtre locale autour de chaque dépôt (cumulativeDistance ± LY) pour les candidats */
export const PILE_WINDOW_LY = 30;

/** Fenêtre élargie pour le fallback automatique quand aucun enrichissement ±30 LY */
export const PILE_WINDOW_FALLBACK_LY = 60;

/**
 * Retourne les candidats dans une fenêtre centrée sur cumulativeDistance.
 * Utile pour le fallback automatique avec fenêtre élargie.
 */
export function getCandidatesInWindow(
  jumps: SpanshJump[],
  centerCumulative: number,
  windowLy: number
): LocalPileCandidate[] {
  if (!jumps?.length) return [];
  let cumulative = 0;
  const result: LocalPileCandidate[] = [];
  const minCumul = centerCumulative - windowLy;
  const maxCumul = centerCumulative + windowLy;
  for (const j of jumps) {
    const d = typeof j.distance === 'number' ? j.distance : 0;
    cumulative += d;
    if (cumulative >= minCumul && cumulative <= maxCumul) {
      result.push({
        name: j.name ?? '',
        distance: d,
        cumulativeDistance: cumulative,
        body_count: j.body_count,
        landmark_value: j.landmark_value,
        estimated_scan_value: j.estimated_scan_value,
        estimated_mapping_value: j.estimated_mapping_value
      });
    }
  }
  return result;
}

/**
 * Analyse une route de colonisation pour extraire les métriques de base et identifier les candidats PILE.
 */
export function analyzeColonisationRoute(jumps: SpanshJump[]): ColonisationRouteAnalysis {
  if (!jumps || jumps.length === 0) {
    return {
      jumps: [],
      summary: { totalJumps: 0, totalDistance: 0, pileCandidates: 0 },
      localCandidatesByPile: []
    };
  }

  let cumulative = 0;
  let referenceAnchor = 0;
  let pileCandidates = 0;
  let pileIndex = 0;
  const analyzed: AnalyzedJump[] = [];
  const jumpsWithCumul: Array<{
    name: string;
    distance: number;
    cumulativeDistance: number;
    body_count?: number;
    landmark_value?: number;
    estimated_scan_value?: number;
    estimated_mapping_value?: number;
  }> = [];

  for (let i = 0; i < jumps.length; i++) {
    const jump = jumps[i];
    const distance = typeof jump.distance === 'number' ? jump.distance : 0;
    cumulative += distance;

    const distanceFromAnchor = cumulative - referenceAnchor;
    let type: AnalyzedJump['type'] = 'TABLIER';

    if (i === 0) {
      type = 'START';
    } else if (i === jumps.length - 1) {
      type = 'END';
    } else if (distanceFromAnchor >= PILE_MIN_LY && distanceFromAnchor <= PILE_MAX_LY) {
      type = 'PILE';
      pileCandidates++;
      pileIndex++;
      referenceAnchor = cumulative;
    }

    jumpsWithCumul.push({
      name: jump.name ?? '',
      distance,
      cumulativeDistance: cumulative,
      body_count: jump.body_count,
      landmark_value: jump.landmark_value,
      estimated_scan_value: jump.estimated_scan_value,
      estimated_mapping_value: jump.estimated_mapping_value
    });

    analyzed.push({
      index: i,
      name: jump.name ?? '',
      distance,
      cumulativeDistance: cumulative,
      distanceFromLastAnchor: distanceFromAnchor,
      pileIndex: type === 'PILE' ? pileIndex : undefined,
      type
    });
  }

  const piles = analyzed.filter((j) => j.type === 'PILE');
  const localCandidatesByPile: PileLocalCandidates[] = piles.map((pile) => {
    const minCumul = pile.cumulativeDistance - PILE_WINDOW_LY;
    const maxCumul = pile.cumulativeDistance + PILE_WINDOW_LY;
    const pileJump = jumps[pile.index];
    const candidates = jumpsWithCumul.filter(
      (j) => j.cumulativeDistance >= minCumul && j.cumulativeDistance <= maxCumul
    );
    return {
      pileIndex: pile.pileIndex!,
      pileName: pile.name,
      pileCumulativeDistance: pile.cumulativeDistance,
      pileX: typeof pileJump?.x === 'number' ? pileJump.x : undefined,
      pileY: typeof pileJump?.y === 'number' ? pileJump.y : undefined,
      pileZ: typeof pileJump?.z === 'number' ? pileJump.z : undefined,
      candidates: candidates.map((c) => ({
        name: c.name,
        distance: c.distance,
        cumulativeDistance: c.cumulativeDistance,
        body_count: c.body_count,
        landmark_value: c.landmark_value,
        estimated_scan_value: c.estimated_scan_value,
        estimated_mapping_value: c.estimated_mapping_value
      }))
    };
  });

  return {
    jumps: analyzed,
    summary: {
      totalJumps: jumps.length,
      totalDistance: cumulative,
      pileCandidates
    },
    localCandidatesByPile
  };
}
