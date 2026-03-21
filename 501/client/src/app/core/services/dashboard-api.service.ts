import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { DashboardResponseDto } from '../models/dashboard.model';
import { CurrentGuildService } from './current-guild.service';

@Injectable({ providedIn: 'root' })
export class DashboardApiService {
  private readonly http = inject(HttpClient);
  private readonly currentGuild = inject(CurrentGuildService);
  private readonly base = '/api';

  getDashboard(commanderName: string | null): Observable<DashboardResponseDto> {
    const guildId = this.currentGuild.guildId();
    const params: Record<string, string | number> = { guildId };
    if (commanderName) params['commanderName'] = commanderName;
    const qs = new URLSearchParams(params as Record<string, string>).toString();
    return this.http.get<DashboardResponseDto>(`${this.base}/guild/dashboard?${qs}`);
  }
}
