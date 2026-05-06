import type { GuildSystemBgsDto } from '../models/guild-systems.model';
import { inaraAgeDays } from './inara-freshness.util';

/** État dérivé pour l'UI (âge Inara, catégorie « sans nouvelles », libellé « Rumeurs »). */
export interface InaraDataDerivation {
  freshnessDays: number | null;
  isWithoutNews: boolean;
  isRumor: boolean;
}

export function getInaraDataDerivation(sys: GuildSystemBgsDto): InaraDataDerivation {
  const freshnessDays = inaraAgeDays(sys.lastUpdated);
  return {
    freshnessDays,
    isWithoutNews: freshnessDays !== null && freshnessDays > 30,
    isRumor: shouldShowRumorsInsteadOfStatus(sys),
  };
}

/** Données Inara trop anciennes pour la catégorie « Systèmes sans nouvelles » (> 30 j). Sans date connue → false. */
export function isInaraWithoutNewsCategory(sys: Pick<GuildSystemBgsDto, 'lastUpdated'>): boolean {
  const age = inaraAgeDays(sys.lastUpdated);
  return age !== null && age > 30;
}

/** Présence d'un statut BGS/Inara affichable (hors remplacement par « Rumeurs »). */
export function hasInaraStatusForRumorDisplay(sys: GuildSystemBgsDto): boolean {
  if (sys.states?.length) return true;
  if (sys.state?.trim()) return true;
  if (sys.isExpansionCandidate) return true;
  if (sys.isThreatened) return true;
  return false;
}

/** 7–30 j inclus + statut → afficher « Rumeurs » à la place du statut brut. */
export function shouldShowRumorsInsteadOfStatus(sys: GuildSystemBgsDto): boolean {
  const age = inaraAgeDays(sys.lastUpdated);
  if (age === null) return false;
  if (age < 7 || age > 30) return false;
  return hasInaraStatusForRumorDisplay(sys);
}

/** Résumé du statut Inara d'origine (tooltip sous « Rumeurs »). */
export function getOriginalInaraStatusSummary(sys: GuildSystemBgsDto): string {
  const parts: string[] = [];
  const fromStates = sys.states?.length ? sys.states : sys.state?.trim() ? [sys.state.trim()] : [];
  for (const s of fromStates) {
    if (s && !parts.includes(s)) parts.push(s);
  }
  if (sys.isExpansionCandidate && !parts.includes('Expansion')) parts.push('Expansion');
  if (sys.isThreatened && !parts.includes('Menacé')) parts.push('Menacé');
  return parts.join(' · ');
}
