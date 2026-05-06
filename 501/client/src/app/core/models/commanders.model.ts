export interface CommanderDto {
  name: string;
  avatarUrl: string | null;
  role: string | null;
  lastSyncedAt: string | null;
  inaraUrl: string | null;
}

export interface CommandersResponseDto {
  commanders: CommanderDto[];
  lastSyncedAt: string | null;
  dataSource: 'live' | 'cached';
}
