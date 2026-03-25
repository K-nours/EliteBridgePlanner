import { Injectable, inject, isDevMode } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

const API_BASE = isDevMode() ? 'https://localhost:7294/api' : '/api';

export interface EdsmJournalTestUploadResultDto {
  success: boolean;
  error?: string | null;
  httpStatus?: number | null;
  msgNum?: number | null;
  msg?: string | null;
  dateUsed?: string | null;
  detail?: string | null;
  event?: string | null;
  starSystem?: string | null;
}

export interface EdsmJournalSettingsDto {
  commanderName: string;
  apiKeyConfigured: boolean;
}

@Injectable({ providedIn: 'root' })
export class EdsmJournalApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE}/edsm/journal`;

  getJournalSettings(): Observable<EdsmJournalSettingsDto> {
    return this.http.get<EdsmJournalSettingsDto>(`${this.base}/settings`);
  }

  /** Si `apiKey` est omis, la clé déjà enregistrée sur le serveur est conservée. */
  putJournalSettings(body: { commanderName: string; apiKey?: string }): Observable<{ saved: boolean }> {
    return this.http.put<{ saved: boolean }>(`${this.base}/settings`, body);
  }

  /** Envoie une ligne FSDJump/Location du raw Frontier vers EDSM (test). */
  testUpload(body?: { date?: string; systemName?: string }): Observable<EdsmJournalTestUploadResultDto> {
    return this.http.post<EdsmJournalTestUploadResultDto>(`${this.base}/test-upload`, body ?? {});
  }
}
