import type { FrontierChantiersInspectResponse, FrontierProfileParseResult } from '../models/frontier-chantiers-inspect.model';

export type ChantierDeclarePlan =
  | { action: 'add' }
  | { action: 'not_docked' }
  | { action: 'missing_location' }
  | { action: 'capi_failed'; message: string }
  | { action: 'no_normalized_profile' };

/**
 * Interprète le profil CAPI normalisé pour savoir si on peut déclarer un chantier (docké + système + station).
 */
export function planChantierDeclarationFromInspect(
  res: FrontierChantiersInspectResponse,
): { plan: ChantierDeclarePlan; profile: FrontierProfileParseResult | null } {
  if (!res.ok) {
    return {
      plan: { action: 'capi_failed', message: res.error ?? 'Réponse CAPI invalide' },
      profile: res.normalizedFromProfile,
    };
  }

  const n = res.normalizedFromProfile;
  if (!n) {
    return { plan: { action: 'no_normalized_profile' }, profile: null };
  }

  if (n.isDocked === false) {
    return { plan: { action: 'not_docked' }, profile: n };
  }

  const system = n.lastSystemName?.trim();
  const station = n.stationName?.trim();
  if (!system || !station) {
    return { plan: { action: 'missing_location' }, profile: n };
  }

  // docked === true, ou inconnu mais station + système présents (contexte station typique du CAPI)
  return { plan: { action: 'add' }, profile: n };
}
