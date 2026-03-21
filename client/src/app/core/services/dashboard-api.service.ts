import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { DashboardResponseDto } from '../models/dashboard.model';
@Injectable({ providedIn: 'root' })
export class DashboardApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  getDashboard(commanderName: string | null): Observable<DashboardResponseDto> {
    const params: Record<string, string> = {};
    if (commanderName) params['commanderName'] = commanderName;
    const qs = new URLSearchParams(params).toString();
    return this.http.get<DashboardResponseDto>(`${this.base}/guild/dashboard${qs ? '?' + qs : ''}`);
  }
}
