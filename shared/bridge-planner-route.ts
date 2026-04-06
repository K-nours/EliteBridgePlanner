/**
 * Source de vérité des couleurs « BridgePlanner » alignée sur le thème par défaut
 * `elitebridgeplanner.client/src/styles/themes/_theme-blue.scss` (body.theme-blue).
 * Toute évolution des hex côté thème doit être répercutée ici pour rester cohérent avec la carte 501.
 */

/** Variantes sémantiques pour l’affichage carte (pont + statut opérationnel). */
export type BridgeRoutePointKind = 'pile' | 'tablier' | 'systeme_operationnel' | 'debut' | 'fin';

/**
 * Hex canoniques thème bleu — miroir strict de _theme-blue.scss (--color-*, --status-*).
 */
export const BRIDGE_PLANNER_THEME_BLUE_HEX: Record<BridgeRoutePointKind, string> = {
  pile: '#aa66ff', // --color-pile
  tablier: '#00d4ff', // --color-tablier
  systeme_operationnel: '#00cc66', // --status-done (opérationnel / colonisation terminée)
  debut: '#00ff88', // --color-debut
  fin: '#ff3366', // --color-fin
};

export type StarSystemLike = { type: string; status: string };

/**
 * Même logique que les cartes / badges BridgePlanner : type + statut → sémantique couleur.
 */
export function bridgeRouteKindFromSystem(system: StarSystemLike): BridgeRoutePointKind {
  const t = system.type?.toUpperCase?.() ?? '';
  const st = system.status?.toUpperCase?.() ?? '';
  if (t === 'DEBUT') return 'debut';
  if (t === 'FIN') return 'fin';
  if (t === 'PILE') return 'pile';
  if (t === 'TABLIER') {
    if (st === 'FINI') return 'systeme_operationnel';
    return 'tablier';
  }
  return 'tablier';
}

export function colorHexForBridgeRouteKind(kind: BridgeRoutePointKind): string {
  return BRIDGE_PLANNER_THEME_BLUE_HEX[kind];
}

/**
 * Point de route — contrat API (`lat` = ED Z, `lng` = ED X, `y` = ED Y), Ly.
 * Extension : `debut` / `fin` pour le rendu BridgePlanner complet.
 */
export interface BridgeRoutePoint {
  id: string;
  lat: number;
  lng: number;
  y: number;
  type: BridgeRoutePointKind;
  colorHex: string;
}

/** Alias historique pour les imports existants. */
export type BridgePlannerRoutePointPayload = BridgeRoutePoint;

export interface BridgeRoute {
  points: BridgeRoutePoint[];
  source?: string;
}

/** Alias historique. */
export type BridgePlannerRoutePayload = BridgeRoute;

/**
 * EDSM x,y,z (Ly) → lat/lng pour le contrat (lng = X, lat = Z, y = Y), aligné carte 501 (THREE: ED_TO_SCENE_X(lng), y, lat).
 */
export function buildRoutePayloadFromSystems(
  systemsOrdered: StarSystemLike[],
  coordsByIndex: Array<{ x: number; y: number; z: number } | null>,
): BridgeRoute {
  const points: BridgeRoutePoint[] = [];
  for (let i = 0; i < systemsOrdered.length; i++) {
    const sys = systemsOrdered[i];
    const c = coordsByIndex[i];
    if (!sys || !c) continue;
    const kind = bridgeRouteKindFromSystem(sys);
    points.push({
      id: `bp-${i}-${kind}`,
      lng: c.x,
      lat: c.z,
      y: c.y,
      type: kind,
      colorHex: colorHexForBridgeRouteKind(kind),
    });
  }
  return { points, source: 'BridgePlanner' };
}
