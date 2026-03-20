import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { GuildSystemsResponseDto } from '../models/guild-systems.model';

@Injectable({ providedIn: 'root' })
export class GuildSystemsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';
  private readonly guildId = 1;

  getSystems(): Observable<GuildSystemsResponseDto> {
    return this.http.get<GuildSystemsResponseDto>(`${this.base}/guild/systems?guildId=${this.guildId}`);
  }
}
