import type { FrontierChantiersInspectSessionInfo } from './frontier-chantiers-inspect.model';

export interface FrontierConstructionResourceItem {
  name: string;
  required: number;
  provided: number;
  remaining: number;
}

export interface FrontierMarketBusinessSummary {
  stationName: string | null;
  marketId: string | null;
  hasConstructionResources: boolean;
  constructionResourcesCount: number;
  constructionResourcesSample: string[];
  constructionResources: FrontierConstructionResourceItem[];
}

/** GET /api/integrations/frontier/chantiers-declare-evaluate */
export interface FrontierChantiersDeclareEvaluateResponse {
  ok: boolean;
  error: string | null;
  canDeclareChantier: boolean;
  userMessage: string;
  systemName: string | null;
  stationName: string | null;
  marketId: string | null;
  commanderName: string | null;
  marketSummary: FrontierMarketBusinessSummary | null;
  profileHttpStatus: number;
  marketHttpStatus: number;
  sessionInfo: FrontierChantiersInspectSessionInfo;
}
