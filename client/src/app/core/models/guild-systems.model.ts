export interface GuildSystemBgsDto {
  id: number;
  name: string;
  influencePercent: number;
  influenceDelta24h?: number;
  state?: string;
  isThreatened: boolean;
  isExpansionCandidate: boolean;
  isHeadquarter: boolean;
  isClean: boolean;
  category: string;
  lastUpdated?: string;
}

export type GuildSystemsDataSource = 'seed' | 'cached';

export interface GuildSystemsResponseDto {
  origin: GuildSystemBgsDto[];
  headquarter: GuildSystemBgsDto[];
  others: GuildSystemBgsDto[];
  dataSource: GuildSystemsDataSource;
}
