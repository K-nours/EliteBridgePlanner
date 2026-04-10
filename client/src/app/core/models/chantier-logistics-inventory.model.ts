/** GET /api/integrations/frontier/chantiers-logistics-inventory */
export interface ChantierLogisticsInventoryDto {
  shipCargoByName: Record<string, number>;
  carrierCargoByName: Record<string, number>;
  fetchedAtUtc: string;
  shipCargoError: string | null;
  carrierCargoError: string | null;
}
