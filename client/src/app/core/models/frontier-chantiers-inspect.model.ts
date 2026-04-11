/** Réponse GET /api/integrations/frontier/chantiers-inspect (debug MVP chantiers). */
export interface FrontierChantiersInspectResponse {
  ok: boolean;
  error: string | null;
  fetchedAtUtc: string;
  capEndpoint: string;
  httpStatus: number;
  /** Taille du JSON CAPI /profile côté serveur (caractères). */
  approxProfileJsonChars: number;
  /** Nombre total de clés à la racine du document /profile. */
  rootKeyCount: number;
  normalizedFromProfile: FrontierProfileParseResult | null;
  parseError: string | null;
  rootKeys: string[];
  propertyPathsSample: string[];
  keywordHits: string[];
  /** Jusqu’à 20 chemins dock/station/starport/location/port — diagnostic compact serveur. */
  dockStationPathCandidates?: FrontierJsonPathCandidate[];
  diagnostic: FrontierChantiersDiagnostic;
  rawJsonFormattedTruncated: string | null;
  /** Diagnostic sans secret : écart dashboard (cache SQL) vs token OAuth en mémoire. */
  sessionInfo: FrontierChantiersInspectSessionInfo;
}

export interface FrontierChantiersInspectSessionInfo {
  oauthTokenInProcessMemory: boolean;
  hasAccessToken: boolean;
  hasRefreshToken: boolean;
  accessTokenProbablyExpiredLocalEstimate: boolean;
  sqlCachedFrontierProfileRowExists: boolean;
  sqlCachedProfileLastFetchedUtc: string | null;
  howDashboardGetsFrontierDataSummary: string;
  chantiersInspectBlockedReason: string;
  appUsesPerUserCookieAuth: boolean;
  architectureNote: string;
  /** Ligne FrontierOAuthSessions présente (jetons chiffrés côté serveur). */
  persistedOAuthSessionRowExists: boolean;
  persistedSessionUpdatedUtc: string | null;
  /** live_memory | live_restored | live_refreshed | cache_only | reconnect_required */
  tokenResolutionMode: string;
  persistenceSummaryNote: string;
}

export interface FrontierProfileParseResult {
  frontierCustomerId: string;
  commanderName: string;
  squadronName: string | null;
  lastSystemName: string | null;
  shipName: string | null;
  /** null si le JSON ne fournit pas le champ */
  isDocked: boolean | null;
  stationName: string | null;
}

export interface FrontierChantiersDiagnostic {
  endpointsInspected: string[];
  usefulFieldsFound: string[];
  fieldsMissingForConstructionTracking: string[];
  note: string;
}

/** Candidat JSON pour diagnostic (pas de document brut). */
export interface FrontierJsonPathCandidate {
  path: string;
  valueType: string;
  valuePreview: string;
}
