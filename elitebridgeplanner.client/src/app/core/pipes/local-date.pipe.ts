import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from '@core/services/language.service';

@Pipe({
  name: 'localDate',
  standalone: true
})
export class LocalDatePipe implements PipeTransform {
  private readonly languageService = inject(LanguageService);

  /**
   * Transforme une date UTC en format local
   *
   * Utilisation dans le template :
   * {{ system.updatedAt | localDate:'short' }}
   * {{ system.createdAt | localDate:'full' }}
   *
   * Format options:
   * - 'short'   : 05/03/2025 10:30
   * - 'medium'  : 5 Mar 2025 10:30:45
   * - 'long'    : 5 March 2025 at 10:30:45 UTC+1
   * - 'full'    : Wednesday, 5 March 2025 at 10:30:45 Central European Time
   */
  transform(value: string | Date | null, format: string = 'short'): string {
    if (!value) return '';

    const date = typeof value === 'string' ? new Date(value) : value;
    if (isNaN(date.getTime())) return '';

    const lang = this.languageService.language();
    const locale = lang === 'fr-FR' ? 'fr-FR' : 'en-GB';

    // Note: Les navigateurs convertissent automatiquement UTC → timezone locale
    // Donc new Date("2025-03-05T10:00:00Z") affiche déjà l'heure locale
    return new Intl.DateTimeFormat(locale, this.getFormatOptions(format)).format(date);
  }

  private getFormatOptions(format: string): Intl.DateTimeFormatOptions {
    switch (format) {
      case 'short':
        return {
          year: 'numeric',
          month: '2-digit',
          day: '2-digit',
          hour: '2-digit',
          minute: '2-digit'
        };
      case 'medium':
        return {
          year: 'numeric',
          month: 'short',
          day: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit'
        };
      case 'long':
        return {
          year: 'numeric',
          month: 'long',
          day: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit',
          timeZoneName: 'short'
        };
      case 'full':
        return {
          weekday: 'long',
          year: 'numeric',
          month: 'long',
          day: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit',
          timeZoneName: 'long'
        };
      default:
        return {};
    }
  }
}
