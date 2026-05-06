import type { DeclaredChantierListItemApi } from '../models/declared-chantiers-api.model';
import type { ActiveChantierSite } from '../state/active-chantiers.store';

export function mapDeclaredListItemApiToSite(d: DeclaredChantierListItemApi): ActiveChantierSite {
  return {
    id: String(d.id),
    systemName: d.systemName,
    stationName: d.stationName,
    cmdrName: d.cmdrName,
    active: d.active,
    declaredAt: d.declaredAtUtc,
    marketId: d.marketId,
    constructionResources: d.constructionResources.map((r) => ({
      name: r.name,
      required: r.required,
      provided: r.provided,
      remaining: r.remaining,
    })),
    constructionResourcesTotal: d.constructionResourcesTotal,
  };
}
