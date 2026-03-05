import { Injectable, signal, effect, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { THEMES, ThemeId, THEME_STORAGE_KEY, Theme } from './theme.model';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  // Signal réactif — tout composant qui lit activeTheme() se met à jour automatiquement
  readonly activeTheme = signal<ThemeId>(this.loadSavedTheme());
  readonly themes = THEMES;

  constructor() {
    // Effect : applique le thème au DOM à chaque changement du signal
    effect(() => {
      this.applyTheme(this.activeTheme());
    });
  }

  setTheme(id: ThemeId): void {
    localStorage.setItem(THEME_STORAGE_KEY, id);
    this.activeTheme.set(id);
  }

  getTheme(id: ThemeId): Theme {
    return THEMES.find(t => t.id === id) ?? THEMES[0];
  }

  private applyTheme(id: ThemeId): void {
    const body = this.document.body;
    // Retirer tous les thèmes existants
    THEMES.forEach(t => body.classList.remove(`theme-${t.id}`));
    // Appliquer le nouveau
    body.classList.add(`theme-${id}`);
  }

  private loadSavedTheme(): ThemeId {
    const saved = localStorage.getItem(THEME_STORAGE_KEY) as ThemeId | null;
    return saved && THEMES.some(t => t.id === saved) ? saved : 'blue';
  }
}
