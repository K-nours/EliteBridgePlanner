import { Injectable, signal } from '@angular/core';

declare global {
  interface Window {
    __INARA_SYNC_BRIDGE__?: boolean;
  }
}

const DEBUG = true;

/**
 * Détecte si le userscript Inara Sync est actif sur le dashboard.
 * Une page web ne peut pas détecter Tampermonkey directement ; le userscript
 * compagnon expose window.__INARA_SYNC_BRIDGE__ = true quand il s'exécute.
 */
@Injectable({ providedIn: 'root' })
export class InaraSyncBridgeService {
  private readonly _available = signal<boolean | null>(null);

  /** true si le script est détecté, false sinon, null si pas encore vérifié */
  readonly available = this._available.asReadonly();

  /** Vérifie une fois si le bridge est présent (cache). À appeler au chargement. */
  check(): void {
    if (this._available() !== null) return;
    this.checkNow();
  }

  /**
   * Re-vérifie immédiatement le bridge (pas de cache).
   * À appeler à chaque clic Sync car le userscript peut s'injecter après le bootstrap Angular.
   * Détecte le signal via le DOM (document.documentElement) car Tampermonkey peut s'exécuter
   * dans un contexte isolé où window n'est pas partagé avec la page.
   */
  checkNow(): boolean {
    const ok =
      typeof document !== 'undefined' &&
      document.documentElement.getAttribute('data-inara-sync-bridge') === 'true';
    this._available.set(ok);
    if (DEBUG && typeof console !== 'undefined') {
      console.log('[InaraBridge] available =', ok);
    }
    return ok;
  }

  /** Vérifie de façon synchrone si le script est disponible. Re-check à chaque appel. */
  isAvailable(): boolean {
    return this.checkNow();
  }
}
