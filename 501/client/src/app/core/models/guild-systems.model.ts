export interface GuildSystemBgsDto {
  id: number;
  name: string;
  influencePercent: number;
  influenceDelta72h?: number;
  state?: string;
  /** États BGS multiples (War, Civil War, Expansion, etc.). Utilisé pour les badges. */
  states?: string[];
  isThreatened: boolean;
  isExpansionCandidate: boolean;
  isHeadquarter: boolean;
  isUnderSurveillance: boolean;
  isClean: boolean;
  category: string;
  lastUpdated?: string;
  /** false = données issues d'une sync réelle, affichées. true = seed, jamais affiché. */
  isFromSeed: boolean;
  /** URL Inara du système (ex: https://inara.cz/elite/starsystem/114520/). Si présente, ouverture directe au clic. */
  inaraUrl?: string | null;
  /** Coordonnées galactiques EDSM (pour carte 3D). Null si non enrichi. */
  coordsX?: number | null;
  coordsY?: number | null;
  coordsZ?: number | null;
  /** Classe spectrale du corps principal (ex. « K », « M », « F ») — carte 3D si exposé par l’API. */
  primaryStarClass?: string | null;
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
  surveillance: GuildSystemBgsDto[];
  conflicts: GuildSystemBgsDto[];
  critical: GuildSystemBgsDto[];
  low: GuildSystemBgsDto[];
  healthy: GuildSystemBgsDto[];
  others: GuildSystemBgsDto[];
  dataSource: GuildSystemsDataSource;
  influenceThresholds: InfluenceThresholdsDto;
  tacticalThresholds?: TacticalThresholdsDto;
}

export type SystemsFilterValue =
  | 'all'
  | 'origin'
  | 'hq'
  | 'surveillance'
  | 'healthy'
  | 'conflicts'
  | 'low'
  | 'critical'
  | 'others'
  | 'withoutNews';
