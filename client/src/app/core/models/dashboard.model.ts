export interface CmdrDto {
  name: string;
  avatarUrl: string | null;
  isCurrentUser: boolean;
}

export interface DashboardResponseDto {
  factionName: string;
  squadronName: string;
  currentCommanderName: string | null;
  cmdrs: CmdrDto[];
}
