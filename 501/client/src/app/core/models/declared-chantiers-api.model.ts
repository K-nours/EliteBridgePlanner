export interface DeclaredChantierResourceApi {
  name: string;
  required: number;
  provided: number;
  remaining: number;
}

export interface DeclaredChantierListItemApi {
  id: number;
  cmdrName: string;
  systemName: string;
  stationName: string;
  marketId: string | null;
  active: boolean;
  declaredAtUtc: string;
  updatedAtUtc: string;
  constructionResources: DeclaredChantierResourceApi[];
  constructionResourcesTotal: number;
}

export interface DeclaredChantierPersistBody {
  systemName: string;
  stationName: string;
  marketId: string | null;
  commanderName: string | null;
  constructionResources: DeclaredChantierResourceApi[];
  constructionResourcesTotal: number;
}

/** Réponse POST …/chantiers-declared/refresh-all */
export interface DeclaredChantierRefreshAllResultApi {
  updated: number;
  deactivated: number;
  skipped: number;
  elapsedMs: number;
  note: string | null;
}
