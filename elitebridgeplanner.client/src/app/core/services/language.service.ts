import { Injectable, signal, effect, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { TranslateService } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

export interface UserPreferences {
  preferredLanguage: string;
  preferredTimeZone: string;
}

const LANGUAGE_STORAGE_KEY = 'elite_bridge_language';
const TIMEZONE_STORAGE_KEY = 'elite_bridge_timezone';
const SUPPORTED_LANGUAGES = ['en-GB', 'fr-FR'];
const DEFAULT_LANGUAGE = 'en-GB';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly http = inject(HttpClient);
  private readonly translate = inject(TranslateService);

  // Signaux pour la langue et timezone
  private readonly _language = signal<string>(this.detectInitialLanguage());
  private readonly _timeZone = signal<string>(this.getInitialTimeZone());

  readonly language = this._language.asReadonly();
  readonly timeZone = this._timeZone.asReadonly();

  constructor() {
    // Initialiser la langue par défaut
    this.translate.setDefaultLang(DEFAULT_LANGUAGE);

    // Charger la langue détectée IMMÉDIATEMENT au démarrage
    const initialLanguage = this._language();
    if (initialLanguage && SUPPORTED_LANGUAGES.includes(initialLanguage)) {
      this.translate.use(initialLanguage);
    }

    // Observer : chaque fois que la langue change, met à jour le traducteur
    effect(() => {
      const lang = this._language();
      // Vérifier que la langue est valide avant de l'utiliser
      if (lang && SUPPORTED_LANGUAGES.includes(lang)) {
        this.translate.use(lang);
        localStorage.setItem(LANGUAGE_STORAGE_KEY, lang);
      }
    });

    // Observer : chaque fois que la timezone change, stocke-la
    effect(() => {
      const tz = this._timeZone();
      if (tz) {
        localStorage.setItem(TIMEZONE_STORAGE_KEY, tz);
      }
    });
  }

  /**
   * Détecte la langue initiale :
   * 1. localStorage (si existant)
   * 2. navigateur (si fr ou en)
   * 3. défaut: en-GB
   */
  private detectInitialLanguage(): string {
    // Déjà sauvegardée ?
    const saved = localStorage.getItem(LANGUAGE_STORAGE_KEY);
    if (saved && SUPPORTED_LANGUAGES.includes(saved)) return saved;

    // Vérifier le navigateur
    const browserLang = navigator.language; // ex: "fr-FR", "en-US"
    if (browserLang.startsWith('fr')) return 'fr-FR';
    if (browserLang.startsWith('en')) return 'en-GB';

    return DEFAULT_LANGUAGE; // Fallback
  }

  /**
   * Détecte la timezone initiale
   * Utilise Intl pour déterminer la timezone du navigateur
   */
  private getInitialTimeZone(): string {
    const saved = localStorage.getItem(TIMEZONE_STORAGE_KEY);
    if (saved) return saved;

    try {
      // Obtenir la timezone système
      const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
      return timeZone || 'UTC';
    } catch {
      return 'UTC';
    }
  }

  /**
   * Définit la langue et l'envoie au serveur
   */
  setLanguage(language: string): Observable<any> {
    if (!language || !SUPPORTED_LANGUAGES.includes(language)) {
      console.warn(`Invalid language: ${language}`);
      return of(null);
    }
    
    this._language.set(language);
    return this.http.put('/api/user/preferences', {
      preferredLanguage: language,
      preferredTimeZone: null // On ne change que la langue
    }).pipe(
      catchError(() => {
        // En cas d'erreur, on garde la langue côté front
        console.warn(`Failed to update language preference on server`);
        return of(null);
      })
    );
  }

  /**
   * Définit la timezone et l'envoie au serveur
   */
  setTimeZone(timeZone: string): Observable<any> {
    if (!timeZone) {
      console.warn(`Invalid timezone: ${timeZone}`);
      return of(null);
    }
    
    this._timeZone.set(timeZone);
    return this.http.put('/api/user/preferences', {
      preferredLanguage: null, // On ne change que la timezone
      preferredTimeZone: timeZone
    }).pipe(
      catchError(() => {
        console.warn(`Failed to update timezone preference on server`);
        return of(null);
      })
    );
  }

  /**
   * Met à jour les deux préférences
   */
  setPreferences(language: string, timeZone: string): Observable<any> {
    if (!language || !SUPPORTED_LANGUAGES.includes(language)) {
      console.warn(`Invalid language: ${language}`);
      return of(null);
    }
    
    this._language.set(language);
    this._timeZone.set(timeZone);
    return this.http.put('/api/user/preferences', {
      preferredLanguage: language,
      preferredTimeZone: timeZone
    }).pipe(
      catchError(() => {
        console.warn(`Failed to update preferences on server`);
        return of(null);
      })
    );
  }

  /**
   * Initialise la langue et la timezone depuis les préférences utilisateur
   * Appelé après le login avec les données de l'utilisateur
   */
  initializeFromUser(language: string, timeZone: string): void {
    if (language && SUPPORTED_LANGUAGES.includes(language)) {
      this._language.set(language);
    }
    if (timeZone) {
      this._timeZone.set(timeZone);
    }
  }
}
