/** Codes métier renvoyés par POST chantiers-declared/refresh-one (corps JSON 400). */
export const CHANTIER_REFRESH_ERROR_MARKET_NO_CAPI_BLOCK = 'MARKET_NO_CHANTIER_CAPI_BLOCK';

/**
 * CAPI /profile a retourné 422 — l'utilisateur n'est pas en jeu ou n'est pas docké.
 * Ce n'est pas une erreur token ; le cycle Real sync doit être ignoré (pas d'erreur affichée).
 */
export const CHANTIER_REFRESH_ERROR_CAPI_NOT_DOCKED = 'CAPI_NOT_DOCKED';
