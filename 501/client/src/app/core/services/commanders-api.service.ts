import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { CommandersResponseDto } from '../models/commanders.model';

@Injectable({ providedIn: 'root' })
export class CommandersApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  getCommanders(guildId = 1): Observable<CommandersResponseDto> {
    return this.http.get<CommandersResponseDto>(`${this.base}/dashboard/commanders?guildId=${guildId}`);
  }

  syncCommanders(guildId = 1): Observable<{ syncedCount: number }> {
    return this.http.post<{ syncedCount: number }>(`${this.base}/sync/inara/commanders?guildId=${guildId}`, {});
  }
}
