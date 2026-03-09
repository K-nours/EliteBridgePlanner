import { Injectable, signal, effect, Inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';

export type Lang = 'fr' | 'en';
const LANG_STORAGE_KEY = 'elite_bridge_lang';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  readonly currentLang = signal<Lang>(this.loadSavedLang());

  constructor(@Inject(DOCUMENT) private doc: Document) {
    effect(() => {
      const lang = this.currentLang();
      this.doc.documentElement.lang = lang;
    });
  }

  setLang(lang: Lang): void {
    localStorage.setItem(LANG_STORAGE_KEY, lang);
    this.currentLang.set(lang);
  }

  toggleLang(): void {
    const next = this.currentLang() === 'fr' ? 'en' : 'fr';
    this.setLang(next);
  }

  private loadSavedLang(): Lang {
    const saved = localStorage.getItem(LANG_STORAGE_KEY) as Lang | null;
    return saved === 'fr' || saved === 'en' ? saved : 'fr';
  }
}
