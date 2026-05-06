export interface CmdrDto {
  name: string;
  avatarUrl: string | null;
  isCurrentUser: boolean;
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

export interface DashboardResponseDto {
  factionName: string;
  squadronName: string;
  currentCommanderName: string | null;
  cmdrs: CmdrDto[];
  frontierProfile?: FrontierProfileDto | null;
}
