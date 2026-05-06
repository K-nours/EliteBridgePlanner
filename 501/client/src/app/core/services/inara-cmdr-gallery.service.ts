import { Injectable, inject, isDevMode } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of } from 'rxjs';

const API_BASE = isDevMode() ? 'https://localhost:7294/api' : '/api';

@Injectable({ providedIn: 'root' })
export class InaraCmdrGalleryService {
  private readonly http = inject(HttpClient);

  /**
   * Récupère les URLs d'images de la galerie Inara du CMDR.
   * Retourne un tableau vide en cas d'erreur ou si aucune image trouvée.
   */
  fetchGallery(cmdrUrl: string): Observable<string[]> {
    const url = `${API_BASE}/inara/cmdr-gallery?cmdrUrl=${encodeURIComponent(cmdrUrl)}`;
    return this.http.get<{ images: string[] }>(url).pipe(
      map((res) => res.images ?? []),
      catchError(() => of([])),
    );
  }
}
