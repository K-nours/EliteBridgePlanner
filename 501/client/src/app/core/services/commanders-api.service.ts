import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { CommandersResponseDto } from '../models/commanders.model';
import { CurrentGuildService } from './current-guild.service';

@Injectable({ providedIn: 'root' })
export class CommandersApiService {
  private readonly http = inject(HttpClient);
  private readonly currentGuild = inject(CurrentGuildService);
  private readonly base = '/api';

  getCommanders(): Observable<CommandersResponseDto> {
    const guildId = this.currentGuild.guildId();
    return this.http.get<CommandersResponseDto>(`${this.base}/dashboard/commanders?guildId=${guildId}`);
  }

  syncCommanders(): Observable<{ syncedCount: number }> {
    const guildId = this.currentGuild.guildId();
    return this.http.post<{ syncedCount: number }>(`${this.base}/sync/inara/commanders?guildId=${guildId}`, {});
  }
}
