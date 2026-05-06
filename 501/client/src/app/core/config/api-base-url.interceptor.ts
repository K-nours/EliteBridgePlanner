import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { API_BASE_URL } from './api-base-url';

/**
 * Si `API_BASE_URL` est défini (ex. http://localhost:5294 en dev), réécrit les requêtes
 * dont l'URL est relative et commence par `/api` vers ce backend.
 * Les URLs déjà absolues (http/https) ne sont pas modifiées.
 *
 * Évite le 404 du serveur de dev Angular (:4200/:4201) lorsque le proxy /api n'intercepte pas.
 */
export const apiBaseUrlInterceptor: HttpInterceptorFn = (req, next) => {
  const base = inject(API_BASE_URL).trim().replace(/\/$/, '');
  if (!base || !req.url.startsWith('/api')) {
    return next(req);
  }
  return next(req.clone({ url: `${base}${req.url}` }));
};
