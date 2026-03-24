import { Component, inject, signal, HostListener, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { AuthService } from '@core/auth/auth.service';
import { LanguageService } from '@core/services/language.service';

@Component({
  selector: 'app-profile-menu',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  templateUrl: './profile-menu.component.html',
  styleUrl: './profile-menu.component.scss'
})
export class ProfileMenuComponent {
  private readonly el = inject(ElementRef<HTMLElement>);
  readonly authService = inject(AuthService);
  readonly languageService = inject(LanguageService);
  readonly isOpen = signal(false);

  /**
   * Bascule l'état d'ouverture/fermeture du menu
   */
  toggleMenu(): void {
    this.isOpen.update(v => !v);
  }

  /**
   * Change la langue de l'application
   * @param language Langue à appliquer ('en-GB' ou 'fr-FR')
   */
  changeLanguage(language: 'en-GB' | 'fr-FR'): void {
    this.languageService.setLanguage(language).subscribe({
      error: (err: unknown) => console.error('Failed to change language:', err)
    });
    // Fermer le menu après la sélection
    this.isOpen.set(false);
  }

  /**
   * Déconnecte l'utilisateur
   */
  logout(): void {
    this.authService.logout();
  }

  /**
   * Ferme le menu si on clique en dehors
   */
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as Node;
    const host = this.el.nativeElement;
    if (this.isOpen() && host && !host.contains(target)) {
      this.isOpen.set(false);
    }
  }
}
