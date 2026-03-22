/**
 * Règle d'affichage avatar (voir docs/AVATAR-PLACEHOLDER.md) :
 * 1. Avatar personnalisé synchronisé → afficher l'avatar réel
 * 2. Pas d'avatar → afficher ce fallback (placeholder Inara ou asset local)
 * 3. Si le fallback ne charge pas → afficher l'initiale
 *
 * Mettre à jour AVATAR_DEFAULT_FALLBACK_URL quand l'URL du placeholder Inara est identifiée
 * (inspecter une image avatar "par défaut" sur une page Inara).
 *
 * AVATAR_PLACEHOLDER_PATTERNS : garder synchronisé avec inara-sync.user.js.
 * Ces patterns permettent au userscript de ne pas enregistrer le placeholder en base.
 */
export const AVATAR_DEFAULT_FALLBACK_URL: string | null = null;

export const AVATAR_PLACEHOLDER_PATTERNS: readonly string[] = [
  'inara.cz/data/avatars/default',
  'inara.cz/images/default-avatar',
  '/default-avatar',
  'noavatar',
];
