import { InjectionToken } from '@angular/core';

/**
 * Préfixe absolu optionnel pour les appels API (ex. `http://localhost:5294`).
 * Vide = URLs relatives `/api/...` (proxy `ng serve` → backend).
 *
 * Pour un front servi sans proxy, définir avant le bootstrap :
 * `window.__API_BASE_URL__ = 'http://localhost:5294'`
 */
/** Fourni dans `app.config.ts` : `window.__API_BASE_URL__`, sinon base dev (`environment.guildDashboardApiBase`). */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL');

export function getApiBaseUrlFromWindow(): string {
  if (typeof window === 'undefined') return '';
  const raw = (window as unknown as { __API_BASE_URL__?: string }).__API_BASE_URL__;
  if (raw == null || String(raw).trim() === '') return '';
  return String(raw).trim().replace(/\/$/, '');
}

/** Résolution finale pour le token API_BASE_URL (fenêtre prioritaire sur l'environnement). */
export function resolveApiBaseUrl(environment: {
  production: boolean;
  guildDashboardApiBase?: string;
}): string {
  const w = getApiBaseUrlFromWindow();
  if (w) return w;
  const b = environment.guildDashboardApiBase?.trim();
  if (!environment.production && b) return b.replace(/\/$/, '');
  return '';
}

/** Concatène la base API et un chemin absolu commençant par `/`. */
export function buildHttpUrl(apiBase: string, absolutePath: string): string {
  const path = absolutePath.startsWith('/') ? absolutePath : `/${absolutePath}`;
  const base = (apiBase ?? '').trim().replace(/\/$/, '');
  return base === '' ? path : `${base}${path}`;
}
