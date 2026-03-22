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
  /** false = données issues d'une sync réelle, affichées. true = seed, jamais affiché. */
  isFromSeed: boolean;
}

export type GuildSystemsDataSource = 'seed' | 'cached';

/** Seuils d'influence (source unique backend). */
export interface InfluenceThresholdsDto {
  critical: number;
  low: number;
  high: number;
}

export interface GuildSystemsResponseDto {
  origin: GuildSystemBgsDto[];
  headquarter: GuildSystemBgsDto[];
  critical: GuildSystemBgsDto[];
  others: GuildSystemBgsDto[];
  dataSource: GuildSystemsDataSource;
  influenceThresholds: InfluenceThresholdsDto;
}
