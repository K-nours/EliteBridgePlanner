import type { GuildSystemBgsDto } from '../models/guild-systems.model';

export type InaraFreshnessTier = 'fresh' | 'medium' | 'stale' | 'stale15' | 'stale30' | 'unknown';

export interface InaraFreshnessBadge {
  label: string;
  tier: InaraFreshnessTier;
  /** Libellé accessibilité (pas de date ISO brute). */
  ariaLabel: string;
}

/** Âge en jours entiers depuis la dernière mise à jour Inara (min 0). Sans date valide → null. */
export function inaraAgeDays(lastUpdated?: string | null): number | null {
  if (lastUpdated == null || String(lastUpdated).trim() === '') return null;
  const t = new Date(String(lastUpdated)).getTime();
  if (Number.isNaN(t)) return null;
  const diff = Date.now() - t;
  if (diff < 0) return 0;
  return Math.floor(diff / 86_400_000);
}

/**
 * Indicateur de fraîcheur des données Inara (dernière mise à jour connue).
 * Couleurs : 0–2 j fresh, 3–5 j medium, 6–14 j stale, 15–29 j +15 rouge, 30+ j +30 rouge.
 */
export function getInaraFreshnessBadge(sys: GuildSystemBgsDto): InaraFreshnessBadge {
  const raw = sys.lastUpdated;
  if (raw == null || String(raw).trim() === '') {
    return {
      label: '—',
      tier: 'unknown',
      ariaLabel: 'Date de mise à jour Inara inconnue',
    };
  }
  const days = inaraAgeDays(String(raw));
  if (days === null) {
    return {
      label: '—',
      tier: 'unknown',
      ariaLabel: 'Date de mise à jour Inara invalide',
    };
  }

  let tier: InaraFreshnessTier;
  let label: string;
  let ariaLabel: string;

  if (days <= 2) {
    tier = 'fresh';
    label = `${days} j`;
    ariaLabel =
      days === 0
        ? "Données Inara : moins d'un jour"
        : days === 1
          ? 'Données Inara : 1 jour'
          : `Données Inara : ${days} jours`;
  } else if (days <= 5) {
    tier = 'medium';
    label = `${days} j`;
    ariaLabel = `Données Inara : ${days} jours`;
  } else if (days >= 30) {
    tier = 'stale30';
    label = '+30';
    ariaLabel = 'Données Inara : 30 jours ou plus';
  } else if (days >= 15) {
    tier = 'stale15';
    label = '+15';
    ariaLabel = 'Données Inara : au moins 15 jours';
  } else {
    tier = 'stale';
    label = days >= 7 ? '7 j+' : `${days} j`;
    ariaLabel = days >= 7 ? 'Données Inara : au moins 7 jours' : `Données Inara : ${days} jours`;
  }

  return { label, tier, ariaLabel };
}
