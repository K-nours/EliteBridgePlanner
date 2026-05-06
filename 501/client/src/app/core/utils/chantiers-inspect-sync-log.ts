import type { FrontierChantiersInspectResponse } from '../models/frontier-chantiers-inspect.model';
import type { FrontierChantiersDeclareEvaluateResponse } from '../models/frontier-chantiers-declare-evaluate.model';

/** Préfixe unique pour remplacer l'entrée précédente dans SyncLogService (pas d'empilement). */
export const CHANTIERS_INSPECT_LOG_MARKER = '[Chantiers inspect]';

const MAX_LINE = 500;
const MAX_NOTE = 320;
const MAX_ERR = 240;
const MAX_CANDIDATE_PREVIEW = 80;

function trunc(s: string, max: number): string {
  if (s.length <= max) return s;
  return s.slice(0, max) + '… [tronqué]';
}

function formatDockStationCandidatesBlock(
  candidates: FrontierChantiersInspectResponse['dockStationPathCandidates'],
): string {
  const list = candidates ?? [];
  if (list.length === 0) {
    return 'Candidats dock/station : aucun candidat détecté';
  }
  const lines = list.slice(0, 20).map(
    (c) =>
      `  · ${c.path} (${c.valueType}) = ${trunc(c.valuePreview || '—', MAX_CANDIDATE_PREVIEW)}`,
  );
  return ['Candidats dock/station détectés :', ...lines].join('\n');
}

/**
 * Résumé compact uniquement — aucun JSON brut, pas de pretty-print, listes bornées.
 * Une entrée = quelques lignes courtes (affichage puis tronçonnage global côté SyncLogService).
 */
export function formatChantiersInspectSyncLogBlock(
  requestUrl: string,
  res: FrontierChantiersInspectResponse,
): string {
  const okLabel = res.ok ? 'succès' : 'erreur';
  const useful = res.diagnostic.usefulFieldsFound.slice(0, 10).join(' · ') || '—';
  const missing = res.diagnostic.fieldsMissingForConstructionTracking.slice(0, 8).join(' · ') || '—';
  const note = trunc(res.diagnostic.note ?? '', MAX_NOTE);
  const ep = trunc(requestUrl, MAX_LINE);
  const approx = res.approxProfileJsonChars ?? 0;
  const rootN = res.rootKeyCount ?? res.rootKeys?.length ?? 0;

  const si = res.sessionInfo;
  const sessionLines: string[] =
    si != null
      ? [
          `Session: token OAuth en mémoire API=${si.oauthTokenInProcessMemory} · access=${si.hasAccessToken} · refresh=${si.hasRefreshToken} · expiré?(estim.)=${si.accessTokenProbablyExpiredLocalEstimate}`,
          `Persistée SQL: ${si.persistedOAuthSessionRowExists ?? '—'} · MAJ session: ${si.persistedSessionUpdatedUtc ?? '—'} · mode=${si.tokenResolutionMode ?? '—'}`,
          `Cache SQL profil: ${si.sqlCachedFrontierProfileRowExists} · dernier fetch: ${si.sqlCachedProfileLastFetchedUtc ?? '—'}`,
          si.persistenceSummaryNote ? `Persistance: ${trunc(si.persistenceSummaryNote, 420)}` : '',
          `Dashboard: ${trunc(si.howDashboardGetsFrontierDataSummary, 420)}`,
          si.chantiersInspectBlockedReason
            ? `Blocage inspect: ${trunc(si.chantiersInspectBlockedReason, 500)}`
            : '',
          `Auth cookie site: ${si.appUsesPerUserCookieAuth} · ${trunc(si.architectureNote, 360)}`,
        ].filter((line) => line.length > 0)
      : [];

  const dockBlock = formatDockStationCandidatesBlock(res.dockStationPathCandidates);

  const lines: string[] = [
    `${CHANTIERS_INSPECT_LOG_MARKER} ${res.fetchedAtUtc}`,
    `HTTP ${res.httpStatus} · ${okLabel} · ${ep}`,
    ...sessionLines,
    `CAPI ${res.capEndpoint} · corps ~${approx} car. · clés racine: ${rootN} · chemins (échantillon): ${res.propertyPathsSample.length} · mots-clés: ${res.keywordHits.length}`,
    dockBlock,
    `Utiles: ${trunc(useful, 900)}`,
    `Manquants: ${trunc(missing, 700)}`,
    `Note: ${note}`,
  ].filter((line) => line.length > 0);

  if (res.error) lines.push(`Erreur: ${trunc(res.error, MAX_ERR)}`);
  if (res.parseError) lines.push(`Parse: ${trunc(res.parseError, MAX_ERR)}`);

  if (approx > 800_000) {
    lines.push('Réponse trop volumineuse, résumé seulement (mode sécurité).');
  }

  if (res.rawJsonFormattedTruncated) {
    lines.push(`Extrait (tronqué): ${trunc(res.rawJsonFormattedTruncated, 400)}`);
  }

  return lines.join('\n');
}

export function formatChantiersInspectHttpError(requestUrl: string, status: number, message: string): string {
  return [
    `${CHANTIERS_INSPECT_LOG_MARKER} ERREUR HTTP`,
    `URL: ${trunc(requestUrl, MAX_LINE)}`,
    `HTTP ${status} · ${trunc(message, MAX_ERR)}`,
  ].join('\n');
}

/**
 * Résumé court pour « État de la synchronisation » — pas de dump session ni listes de chemins JSON.
 */
export function formatChantiersInspectSyncLogShort(
  requestUrl: string,
  res: FrontierChantiersInspectResponse,
  outcomeLine: string,
): string {
  const n = res.normalizedFromProfile;
  const profilLine = n
    ? `Profil CAPI: CMDR ${n.commanderName || '—'} · docké=${n.isDocked === true ? 'oui' : n.isDocked === false ? 'non' : '—'} · système=${n.lastSystemName ?? '—'} · station=${n.stationName ?? '—'}`
    : 'Profil CAPI normalisé: indisponible';
  return [
    `${CHANTIERS_INSPECT_LOG_MARKER} ${res.fetchedAtUtc}`,
    `GET ${trunc(requestUrl, MAX_LINE)}`,
    `HTTP ${res.httpStatus} · ok=${res.ok}`,
    profilLine,
    formatDockStationCandidatesBlock(res.dockStationPathCandidates),
    outcomeLine,
  ].join('\n');
}

export const CHANTIERS_DECLARE_EVALUATE_LOG_MARKER = '[Chantiers declare]';

export function formatChantiersDeclareEvaluateSyncLog(
  requestUrl: string,
  res: FrontierChantiersDeclareEvaluateResponse,
  outcomeLine: string,
): string {
  const m = res.marketSummary;
  const marketLine = m
    ? `Marché métier: station(name)=${trunc(m.stationName ?? '—', 120)} · marketId=${trunc(m.marketId ?? '—', 80)} · construction×${m.constructionResourcesCount} · échantillon=${(m.constructionResourcesSample ?? []).slice(0, 5).join(', ') || '—'}`
    : 'Marché métier: —';
  return [
    `${CHANTIERS_DECLARE_EVALUATE_LOG_MARKER}`,
    `GET ${trunc(requestUrl, MAX_LINE)}`,
    `HTTP profil=${res.profileHttpStatus} marché=${res.marketHttpStatus} · ok=${res.ok} · canDeclare=${res.canDeclareChantier}`,
    `Système=${res.systemName ?? '—'} · station=${res.stationName ?? '—'} · marketId=${res.marketId ?? '—'}`,
    marketLine,
    trunc(res.userMessage, 200),
    outcomeLine,
  ].join('\n');
}

export function formatChantiersDeclareEvaluateHttpError(requestUrl: string, status: number, message: string): string {
  return [
    `${CHANTIERS_DECLARE_EVALUATE_LOG_MARKER} ERREUR HTTP`,
    `GET ${trunc(requestUrl, MAX_LINE)}`,
    `HTTP ${status} · ${trunc(message, MAX_ERR)}`,
  ].join('\n');
}
