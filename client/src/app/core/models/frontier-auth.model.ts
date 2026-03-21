export type FrontierAuthState = 'not-connected' | 'connected' | 'expired' | 'error';

export interface FrontierAuthStatus {
  state: FrontierAuthState;
  profile: FrontierProfileDto | null;
  errorMessage: string | null;
}

export interface FrontierMeResponse {
  connected: boolean;
  commander?: string;
  squadron?: string;
  customerId?: string;
  guildId?: number | null;
  guildName?: string | null;
}

export interface FrontierProfileDto {
  frontierCustomerId: string;
  commanderName: string;
  squadronName: string | null;
  lastSystemName: string | null;
  shipName: string | null;
  guildId: number | null;
  guildName: string | null;
  lastFetchedAt: string;
}
