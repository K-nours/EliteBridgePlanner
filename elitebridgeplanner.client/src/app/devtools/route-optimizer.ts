/**
 * Optimisation V1 de la route colonisation.
 * Remplacer un dépôt géométrique par un meilleur candidat local si le remplacement
 * est compatible avec la contrainte de saut (≤ 15 LY).
 */

import type {
  ColonisationRouteAnalysis,
  LocalPileCandidate,
  SpanshJump
} from './colonisation-route.analyzer';
import type { AnalyzedJump } from './colonisation-route.analyzer';

/** Contrainte max de saut pour considérer un candidat comme insérable (LY) */
export const MAX_JUMP_LY = 15;

/** Facteur de pénalité pour le coût de détour sur le score ajusté */
export const DEVIATION_COST_FACTOR = 10;

export interface RouteModification {
  depositIndex: number;
  originalSystem: string;
  replacementSystem: string;
  originalScore: number;
  candidateScore: number;
  adjustedScore: number;
  deviationCost: number;
  inserted: boolean;
  /** true si le candidat a des coordonnées pour le calcul de distance */
  hasCoords: boolean;
  /** Distance prev → candidate (LY) */
  distPrevCandidate: number | null;
  /** Distance candidate → next (LY) */
  distCandidateNext: number | null;
}

export interface OptimizedRoute {
  /** Saut analysés avec les remplacements appliqués */
  jumps: AnalyzedJump[];
  /** Modifications enregistrées par dépôt */
  modifications: RouteModification[];
  /** Compteurs globaux */
  stats: {
    depositsAnalyzed: number;
    depositsReplaced: number;
    depositsRejectedNotInsertable: number;
    depositsRejectedLowerScore: number;
    totalScoreGained: number;
  };
}

interface SystemCoords {
  x: number;
  y: number;
  z: number;
}

function hasCoords(o: { x?: number; y?: number; z?: number }): o is SystemCoords {
  return (
    typeof o.x === 'number' &&
    typeof o.y === 'number' &&
    typeof o.z === 'number' &&
    !Number.isNaN(o.x + o.y + o.z)
  );
}

/** Distance 3D en années-lumière (formule standard Elite) */
export function distance3d(a: SystemCoords, b: SystemCoords): number {
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  const dz = a.z - b.z;
  return Math.sqrt(dx * dx + dy * dy + dz * dz);
}

export interface OptimizeRouteInput {
  analysis: ColonisationRouteAnalysis;
  /** Sauts Spansh bruts (avec x,y,z) pour le calcul des distances */
  rawJumps: SpanshJump[];
  /** Meilleur candidat par pileIndex */
  bestPerPile: Map<
    number,
    {
      name: string;
      score: number;
      candidate: LocalPileCandidate;
    }
  >;
  /** Score du dépôt géométrique (pile.pileName) par pileIndex — 0 si inconnu */
  depositScoreByPile: Map<number, number>;
}

/**
 * Optimise la route : remplace les dépôts géométriques par de meilleurs candidats
 * si insertable ET adjustedScore > originalScore.
 */
export function optimizeRoute(input: OptimizeRouteInput): OptimizedRoute {
  const { analysis, rawJumps, bestPerPile, depositScoreByPile } = input;
  const jumps = [...analysis.jumps];
  const piles = analysis.localCandidatesByPile;
  const modifications: RouteModification[] = [];

  const stats = {
    depositsAnalyzed: 0,
    depositsReplaced: 0,
    depositsRejectedNotInsertable: 0,
    depositsRejectedLowerScore: 0,
    totalScoreGained: 0
  };

  if (piles.length === 0) {
    return { jumps, modifications, stats };
  }

  /** Coords d'un saut par index (depuis rawJumps Spansh) */
  const getJumpCoords = (idx: number): SystemCoords | null => {
    const raw = rawJumps[idx];
    return raw && hasCoords(raw) ? { x: raw.x, y: raw.y, z: raw.z } : null;
  };

  /** Lookup coords : pile (dépôt) ou candidats de la fenêtre */
  const getCoordsForPileSystem = (
    pile: (typeof piles)[0],
    systemName: string
  ): SystemCoords | null => {
    if (pile.pileName === systemName && pile.pileX != null && pile.pileY != null && pile.pileZ != null) {
      return { x: pile.pileX, y: pile.pileY, z: pile.pileZ };
    }
    const cand = pile.candidates.find((c) => c.name === systemName);
    return cand && hasCoords(cand) ? { x: cand.x!, y: cand.y!, z: cand.z! } : null;
  };

  for (const pile of piles) {
    const pileJumpIndex = pile.pileIndex; // index 1-based
    const pileAnalyzed = analysis.jumps.find((j) => j.pileIndex === pileJumpIndex);
    const depositJumpIndex = pileAnalyzed?.index ?? -1;

    if (depositJumpIndex < 0) continue;

    stats.depositsAnalyzed++;

    const prevIndex = depositJumpIndex - 1;
    const nextIndex = depositJumpIndex + 1;
    const prevJump = analysis.jumps[prevIndex];
    const nextJump = analysis.jumps[nextIndex];
    const currentDeposit = analysis.jumps[depositJumpIndex];

    if (!prevJump || !nextJump || !currentDeposit) continue;

    const best = bestPerPile.get(pile.pileIndex);
    if (!best) continue;

    const depositScore = depositScoreByPile.get(pile.pileIndex) ?? 0;

    const depositCoords = getCoordsForPileSystem(pile, pile.pileName);
    const candidateCoords = getCoordsForPileSystem(pile, best.candidate.name);
    const prevCoords = getJumpCoords(prevIndex) ?? getCoordsForPileSystem(pile, prevJump.name);
    const nextCoords = getJumpCoords(nextIndex) ?? getCoordsForPileSystem(pile, nextJump.name);

    let distPrevCandidate: number | null = null;
    let distCandidateNext: number | null = null;
    let deviationCost = 0;
    let insertable = false;

    if (
      candidateCoords &&
      prevCoords &&
      nextCoords &&
      depositCoords
    ) {
      distPrevCandidate = distance3d(prevCoords, candidateCoords);
      distCandidateNext = distance3d(candidateCoords, nextCoords);
      insertable = distPrevCandidate <= MAX_JUMP_LY && distCandidateNext <= MAX_JUMP_LY;

      const distPrevDeposit = distance3d(prevCoords, depositCoords);
      const distDepositNext = distance3d(depositCoords, nextCoords);
      deviationCost =
        distPrevCandidate + distCandidateNext - distPrevDeposit - distDepositNext;
    }

    const adjustedScore = best.score - deviationCost * DEVIATION_COST_FACTOR;

    const shouldReplace = insertable && adjustedScore > depositScore;

    modifications.push({
      depositIndex: pile.pileIndex,
      originalSystem: pile.pileName,
      replacementSystem: best.candidate.name,
      originalScore: depositScore,
      candidateScore: best.score,
      deviationCost,
      adjustedScore,
      inserted: shouldReplace,
      hasCoords: !!candidateCoords,
      distPrevCandidate,
      distCandidateNext
    });

    if (!insertable) {
      stats.depositsRejectedNotInsertable++;
    } else if (!shouldReplace) {
      stats.depositsRejectedLowerScore++;
    } else {
      stats.depositsReplaced++;
      stats.totalScoreGained += adjustedScore - depositScore;
      jumps[depositJumpIndex] = {
        ...currentDeposit,
        name: best.candidate.name
      };
    }
  }

  return { jumps, modifications, stats };
}
