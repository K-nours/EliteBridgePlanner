import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { FrontierChantiersDeclareEvaluateResponse } from '../models/frontier-chantiers-declare-evaluate.model';
import { API_BASE_URL, buildHttpUrl } from '../config/api-base-url';

@Injectable({ providedIn: 'root' })
export class FrontierChantiersDeclareEvaluateService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  private readonly path = '/api/integrations/frontier/chantiers-declare-evaluate';

  getEvaluateRequestUrl(): string {
    return buildHttpUrl(this.apiBaseUrl, this.path);
  }

  evaluate(): Observable<FrontierChantiersDeclareEvaluateResponse> {
    return this.http.get<FrontierChantiersDeclareEvaluateResponse>(this.getEvaluateRequestUrl());
  }
}
