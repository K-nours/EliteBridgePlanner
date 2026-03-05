import { Component, inject } from '@angular/core';
import { ThemeService } from '../../themes/theme.service';
import { THEMES, ThemeId } from '../../themes/theme.model';

@Component({
  selector: 'app-theme-selector',
  standalone: true,
  template: `
    <div class="theme-selector">
      <span class="label">THEME</span>
      @for (theme of themes; track theme.id) {
        <button
          class="theme-dot"
          [class.active]="themeService.activeTheme() === theme.id"
          [style.background]="theme.color"
          [title]="theme.label"
          (click)="setTheme(theme.id)"
          [attr.aria-label]="'Thème ' + theme.label"
        ></button>
      }
    </div>
  `,
  styleUrl: './theme-selector.component.scss'
})
export class ThemeSelectorComponent {
  readonly themeService = inject(ThemeService);
  readonly themes = THEMES;

  setTheme(id: ThemeId): void {
    this.themeService.setTheme(id);
  }
}
