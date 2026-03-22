export interface GuildSystemBgsDto {
  id: number;
  name: string;
  influencePercent: number;
  influenceDelta24h?: number;
  state?: string;
  /** États BGS multiples (War, Civil War, Expansion, etc.). Utilisé pour les badges. */
  states?: string[];
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

/** Seuils tactiques pour catégorisation (Critical < 5%, Low < 15%, Healthy ≥ 60%). */
export interface TacticalThresholdsDto {
  critical: number;
  low: number;
  high: number;
}

export interface GuildSystemsResponseDto {
  origin: GuildSystemBgsDto[];
  headquarter: GuildSystemBgsDto[];
  conflicts: GuildSystemBgsDto[];
  critical: GuildSystemBgsDto[];
  low: GuildSystemBgsDto[];
  healthy: GuildSystemBgsDto[];
  others: GuildSystemBgsDto[];
  dataSource: GuildSystemsDataSource;
  influenceThresholds: InfluenceThresholdsDto;
  tacticalThresholds?: TacticalThresholdsDto;
}

export type SystemsFilterValue = 'all' | 'origin' | 'hq' | 'healthy' | 'conflicts' | 'low' | 'critical' | 'others';
