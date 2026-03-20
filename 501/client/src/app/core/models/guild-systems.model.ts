export interface GuildSystemBgsDto {
  id: number;
  name: string;
  influencePercent: number;
  influenceDelta24h?: number;
  state?: string;
  isThreatened: boolean;
  isExpansionCandidate: boolean;
  isClean: boolean;
  category: string;
  lastUpdated?: string;
}

export interface GuildSystemsResponseDto {
  origin: GuildSystemBgsDto[];
  headquarter: GuildSystemBgsDto[];
  others: GuildSystemBgsDto[];
}
