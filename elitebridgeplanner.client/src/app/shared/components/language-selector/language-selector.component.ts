import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { LanguageService } from '@core/services/language.service';

@Component({
  selector: 'app-language-selector',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  template: `
    <select 
      [value]="languageService.language()" 
      (change)="onLanguageChange($event)"
      class="language-select"
      [attr.aria-label]="'nav.language' | translate">
      <option value="en-GB">English</option>
      <option value="fr-FR">Français</option>
    </select>
  `,
  styles: [`
    .language-select {
      padding: 0.5rem 0.75rem;
      border-radius: 4px;
      border: 1px solid #ccc;
      cursor: pointer;
      font-size: 0.95rem;
      background-color: white;
      color: #333;
      transition: border-color 0.3s ease;

      &:hover {
        border-color: #999;
      }

      &:focus {
        outline: none;
        border-color: #0066cc;
        box-shadow: 0 0 0 2px rgba(0, 102, 204, 0.2);
      }
    }
  `]
})
export class LanguageSelectorComponent {
  protected languageService = inject(LanguageService);

  onLanguageChange(event: Event): void {
    const language = (event.target as HTMLSelectElement).value;
    this.languageService.setLanguage(language).subscribe({
      error: (err: unknown) => console.error('Failed to change language:', err)
    });
  }
}
