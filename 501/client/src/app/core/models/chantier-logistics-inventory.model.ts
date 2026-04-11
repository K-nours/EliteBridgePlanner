/** GET /api/integrations/frontier/chantiers-logistics-inventory */
export interface ChantierLogisticsInventoryDto {
  shipCargoByName: Record<string, number>;
  carrierCargoByName: Record<string, number>;
  fetchedAtUtc: string;
  shipCargoError: string | null;
  carrierCargoError: string | null;
  /** CAPI /profile → HTTP 429 */
  shipRateLimited?: boolean;
  /** CAPI /fleetcarrier → HTTP 429 */
  carrierRateLimited?: boolean;
  /** Au moins un 429 sur le cycle serveur */
  rateLimited?: boolean;
  retryAfterSeconds?: number | null;
  /** Pas d’appel FC car /profile déjà en 429 */
  fleetCarrierSkippedDueToProfileRateLimit?: boolean;
}
