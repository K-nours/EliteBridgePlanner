import { Component, inject, signal, HostListener, ElementRef } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { LanguageService } from '../../../core/i18n/language.service';

@Component({
  selector: 'app-profile-menu',
  standalone: true,
  template: `
    <div class="profile-menu" #menuContainer>
      <button type="button" class="avatar-btn" (click)="toggleMenu()" [attr.aria-expanded]="isOpen()"
        [attr.aria-haspopup]="true" aria-label="Menu profil">
        <img class="avatar-img" src="/images/avatar.png" alt="Avatar" />
      </button>
      @if (isOpen()) {
      <div class="profile-dropdown">
        <div class="dropdown-item lang-toggle">
          <div class="cockpit-toggle">
            <button type="button" class="cockpit-toggle-btn" [class.active]="languageService.currentLang() === 'fr'"
              (click)="languageService.setLang('fr')">FR</button>
            <button type="button" class="cockpit-toggle-btn" [class.active]="languageService.currentLang() === 'en'"
              (click)="languageService.setLang('en')">EN</button>
          </div>
        </div>
        <div class="logout-separator"></div>
        <button type="button" class="dropdown-item btn-critical" (click)="logout()">
          DÉCONNEXION
        </button>
      </div>
      }
    </div>
  `,
  styleUrl: './profile-menu.component.scss'
})
export class ProfileMenuComponent {
  private readonly el = inject(ElementRef<HTMLElement>);
  readonly authService = inject(AuthService);
  readonly languageService = inject(LanguageService);
  readonly isOpen = signal(false);

  toggleMenu(): void {
    this.isOpen.update(v => !v);
  }

  logout(): void {
    this.authService.logout();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as Node;
    const host = this.el.nativeElement;
    if (this.isOpen() && host && !host.contains(target)) {
      this.isOpen.set(false);
    }
  }
}
