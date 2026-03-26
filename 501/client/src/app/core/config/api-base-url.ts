import { InjectionToken } from '@angular/core';

/**
 * Préfixe absolu optionnel pour les appels API (ex. `http://localhost:5294`).
 * Vide = URLs relatives `/api/...` (proxy `ng serve` → backend).
 *
 * Pour un front servi sans proxy, définir avant le bootstrap :
 * `window.__API_BASE_URL__ = 'http://localhost:5294'`
 */
/** Fourni dans `app.config.ts` (souvent chaîne vide : proxy `/api` vers le backend). */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL');

export function getApiBaseUrlFromWindow(): string {
  if (typeof window === 'undefined') return '';
  const raw = (window as unknown as { __API_BASE_URL__?: string }).__API_BASE_URL__;
  if (raw == null || String(raw).trim() === '') return '';
  return String(raw).trim().replace(/\/$/, '');
}

/** Concatène la base API et un chemin absolu commençant par `/`. */
export function buildHttpUrl(apiBase: string, absolutePath: string): string {
  const path = absolutePath.startsWith('/') ? absolutePath : `/${absolutePath}`;
  const base = (apiBase ?? '').trim().replace(/\/$/, '');
  return base === '' ? path : `${base}${path}`;
}
