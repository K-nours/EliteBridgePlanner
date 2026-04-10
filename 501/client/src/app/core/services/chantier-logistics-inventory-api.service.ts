import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import type { ChantierLogisticsInventoryDto } from '../models/chantier-logistics-inventory.model';
import { API_BASE_URL, buildHttpUrl } from '../config/api-base-url';

@Injectable({ providedIn: 'root' })
export class ChantierLogisticsInventoryApiService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  private readonly path = '/api/integrations/frontier/chantiers-logistics-inventory';

  getInventory(): Observable<ChantierLogisticsInventoryDto> {
    return this.http.get<ChantierLogisticsInventoryDto>(buildHttpUrl(this.apiBaseUrl, this.path));
  }
}
