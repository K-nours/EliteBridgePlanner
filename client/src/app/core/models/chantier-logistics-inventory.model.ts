/** GET /api/integrations/frontier/chantiers-logistics-inventory */
export interface ChantierLogisticsInventoryDto {
  carrierCargoByName: Record<string, number>;
  fetchedAtUtc: string;
  carrierCargoError: string | null;
  /** CAPI /fleetcarrier → HTTP 429 */
  carrierRateLimited?: boolean;
  /** Au moins un 429 sur le cycle serveur */
  rateLimited?: boolean;
  retryAfterSeconds?: number | null;
}
