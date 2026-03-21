/**
 * Feature archived – no reliable external data source for Faction → Systems → Influence %.
 * Conserver pour R&D futur. Voir docs/GUILD-SYSTEMS.md.
 */
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { GuildSystemsResponseDto } from '../models/guild-systems.model';
import { CurrentGuildService } from './current-guild.service';

/**
 * Service Guild Systems — le frontend déclenche uniquement.
 * Envoie guildId (guilde courante). Le backend décide FactionName/InaraFactionId depuis la Guild en base.
 * Jamais d'identifiant de faction ni de source externe.
 */
@Injectable({ providedIn: 'root' })
export class GuildSystemsApiService {
  private readonly http = inject(HttpClient);
  private readonly currentGuild = inject(CurrentGuildService);
  private readonly base = '/api';

  getSystems(): Observable<GuildSystemsResponseDto> {
    const guildId = this.currentGuild.guildId();
    return this.http.get<GuildSystemsResponseDto>(`${this.base}/guild/systems?guildId=${guildId}`);
  }

  toggleHeadquarter(systemId: number): Observable<void> {
    const guildId = this.currentGuild.guildId();
    return this.http.post<void>(`${this.base}/guild/systems/${systemId}/toggle-headquarter?guildId=${guildId}`, {});
  }

  syncBgs(): Observable<{ updated: number }> {
    const guildId = this.currentGuild.guildId();
    return this.http.post<{ updated: number }>(`${this.base}/guild/systems/sync?guildId=${guildId}`, {});
  }
}
