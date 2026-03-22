/**
 * Seuils métier d'influence — alignés sur les sections tactiques.
 * Source unique pour couleurs et catégorisation.
 *
 * - Critiques : < 5% → rouge fort
 * - Bas : < 15% → blanc
 * - Sains : ≥ 60% → vert
 * - Autres : 15% à 60% → neutre
 */
export const INFLUENCE_TACTICAL_THRESHOLDS = {
  critical: 5,
  low: 15,
  high: 60,
} as const;

export type InfluenceClass = 'influence-critical' | 'influence-low' | 'influence-high' | 'influence-normal';

/**
 * Retourne la classe CSS pour un pourcentage d'influence.
 * Cohérence stricte avec les sections métier (Critiques, Bas, Sains, Autres).
 */
export function getInfluenceClass(influencePercent: number): InfluenceClass {
  const { critical, low, high } = INFLUENCE_TACTICAL_THRESHOLDS;
  if (influencePercent < critical) return 'influence-critical';
  if (influencePercent < low) return 'influence-low';
  if (influencePercent >= high) return 'influence-high';
  return 'influence-normal';
}
