/**
 * Indicateur technique de source de données pour chaque panneau.
 * Permet de savoir si la donnée affichée est live, cached, seed ou mock.
 */
export type DataSourceType = 'live' | 'cached' | 'seed' | 'mock';

export interface DataSourceInfo {
  type: DataSourceType;
  /** Timestamp ISO si pertinent (ex: dernière sync) */
  lastUpdated?: string;
  /** Message optionnel pour debug */
  message?: string;
}

export const DATA_SOURCE_LABELS: Record<DataSourceType, string> = {
  live: 'Live',
  cached: 'Cache',
  seed: 'Seed',
  mock: 'Mock',
};
