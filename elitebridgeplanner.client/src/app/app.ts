import { Component, inject, OnInit, effect } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from './shared/themes/theme.service';
import { LanguageService } from '@core/services/language.service';
import { AuthService } from '@core/auth/auth.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  template: `<router-outlet />`,
  styles: []
})
export class App implements OnInit {
  private readonly themeService = inject(ThemeService);
  private readonly languageService = inject(LanguageService);
  private readonly authService = inject(AuthService);

  constructor() {
    // Observer : quand l'utilisateur se connecte, initialiser la langue
    effect(() => {
      const user = this.authService.currentUser();
      if (user) {
        this.languageService.initializeFromUser(user.preferredLanguage, user.preferredTimeZone);
      }
    });
  }

  ngOnInit(): void {
    // ThemeService et LanguageService appliquent thème/lang via leurs effects au démarrage
  }
}

