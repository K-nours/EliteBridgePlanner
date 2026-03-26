import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL, buildHttpUrl } from '../config/api-base-url';

export interface FrontierJournalUnifiedSyncStatusDto {
  phase: string;
  isRunning: boolean;
  frontierCustomerId?: string | null;
  commanderName?: string | null;
  lastSyncCompletedUtc?: string | null;
  lastMessage?: string | null;
  /** Récap chiffré après un run réussi. */
  summaryMessage?: string | null;
  /** login | relogin — proposer Connecter / Reconnecter Frontier */
  frontierSessionUxAction?: string | null;
  lastError?: string | null;
  fetchedSuccessDaysApprox: number;
  daysParsedThisRun: number;
  newDaysFetchedThisRun: number;
  pendingParseDays: number;
  systemsWithCoordsCount: number;
}

@Injectable({ providedIn: 'root' })
export class FrontierJournalApiService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  /** URL API : chemin relatif `/api/...` (proxy dev) ou absolu si `window.__API_BASE_URL__` est défini. */
  private url(path: string): string {
    const relative = `/api/frontier/journal${path.startsWith('/') ? path : `/${path}`}`;
    return buildHttpUrl(this.apiBaseUrl, relative);
  }

  startUnifiedSync(): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(this.url('/sync'), {});
  }

  getUnifiedSyncStatus(): Observable<FrontierJournalUnifiedSyncStatusDto> {
    return this.http.get<FrontierJournalUnifiedSyncStatusDto>(this.url('/sync/status'));
  }

  getParseStatus(): Observable<FrontierJournalParseStatusDto> {
    return this.http.get<FrontierJournalParseStatusDto>(this.url('/parse/status'));
  }

  getDerivedSystems(): Observable<FrontierJournalDerivedResponseDto> {
    return this.http.get<FrontierJournalDerivedResponseDto>(this.url('/derived/systems'));
  }

  /** GET /api/frontier/journal/export — ZIP du journal local (CMDR connecté). */
  exportJournalBlob(): Observable<Blob> {
    return this.http.get(this.url('/export'), { responseType: 'blob' });
  }

  /** POST /api/frontier/journal/import — multipart. */
  importJournal(
    file: File,
    strategy: 'replace' | 'merge',
    duplicatePolicy: 'skip' | 'import' = 'skip',
  ): Observable<FrontierJournalImportResultDto> {
    const fd = new FormData();
    fd.append('file', file, file.name);
    fd.append('strategy', strategy);
    fd.append('duplicatePolicy', duplicatePolicy);
    return this.http.post<FrontierJournalImportResultDto>(this.url('/import'), fd);
  }
}

export interface FrontierJournalImportResultDto {
  success: boolean;
  message: string;
  strategy?: string;
  rawDayCount?: number;
  mergeAddedDays?: number;
  mergeOverwrittenDays?: number;
  mergeSkippedDuplicateDays?: number;
}

export interface FrontierJournalParseStatusDto {
  isRunning: boolean;
  parseVersion: number;
  pendingDaysEstimate: number;
  parsedDaysCount: number;
  errorDaysCount: number;
  systemsCount: number;
  systemsWithCoordsCount: number;
  derivedUpdatedAt: string | null;
  lastParseError: string | null;
  lastBatchDaysProcessed: number;
  lastBatchNewSystemsCount: number;
  lastBatchNewSystemsWithCoordsCount: number;
  lastBatchNewSystemNames: string[];
}

export interface FrontierJournalDerivedResponseDto {
  parseVersion: number;
  updatedAt: string | null;
  systems: FrontierJournalSystemDerivedDto[];
}

export interface FrontierJournalSystemDerivedDto {
  systemName: string;
  firstVisitedAt: string | null;
  lastVisitedAt: string | null;
  visitCount: number;
  isVisited: boolean;
  hasFirstDiscoveryBody: boolean;
  isFullScanned: boolean;
  provenance: string | null;
  coordsX?: number | null;
  coordsY?: number | null;
  coordsZ?: number | null;
}
