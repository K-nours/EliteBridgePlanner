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
}
