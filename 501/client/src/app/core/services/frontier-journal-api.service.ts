import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface FrontierJournalBackfillStatusDto {
  isRunning: boolean;
  completed: boolean;
  currentDate: string | null;
  startDate: string | null;
  minDate: string | null;
  totalDaysProcessed: number;
  successCount: number;
  emptyCount: number;
  errorCount: number;
  startedAt: string | null;
  updatedAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class FrontierJournalApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/frontier/journal';

  startBackfill(): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.base}/backfill/start`, {});
  }

  getStatus(): Observable<FrontierJournalBackfillStatusDto> {
    return this.http.get<FrontierJournalBackfillStatusDto>(`${this.base}/backfill/status`);
  }

  getRetryErrorsCount(): Observable<{ count: number; datesToRetry: string[] }> {
    return this.http.get<{ count: number; datesToRetry: string[] }>(`${this.base}/backfill/retry-errors`);
  }

  startRetryErrors(): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.base}/backfill/retry-errors`, {});
  }

  stopBackfill(): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.base}/backfill/stop`, {});
  }

  startIncrementalParse(batchSize = 40): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(
      `${this.base}/parse/incremental?batchSize=${batchSize}`,
      {},
    );
  }

  getParseStatus(): Observable<FrontierJournalParseStatusDto> {
    return this.http.get<FrontierJournalParseStatusDto>(`${this.base}/parse/status`);
  }

  getDerivedSystems(): Observable<FrontierJournalDerivedResponseDto> {
    return this.http.get<FrontierJournalDerivedResponseDto>(`${this.base}/derived/systems`);
  }
}

export interface FrontierJournalParseStatusDto {
  isRunning: boolean;
  parseVersion: number;
  pendingDaysEstimate: number;
  parsedDaysCount: number;
  errorDaysCount: number;
  systemsCount: number;
  derivedUpdatedAt: string | null;
  lastParseError: string | null;
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
  isDiscovered: boolean;
  isFullScanned: boolean;
  provenance: string | null;
  /** Coordonnées galactiques (Ly) depuis StarPos du journal si présentes. */
  coordsX?: number | null;
  coordsY?: number | null;
  coordsZ?: number | null;
}
