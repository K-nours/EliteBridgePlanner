import { Pipe, PipeTransform } from '@angular/core';

/**
 * Tronque une chaîne au milieu pour afficher le début et la fin.
 * Ex: "Colonia Bridge Alpha System Very Long Name" → "Colonia B...ng Name"
 */
@Pipe({ name: 'truncateMiddle', standalone: true })
export class TruncateMiddlePipe implements PipeTransform {
  transform(value: string | null | undefined, maxLength: number = 24): string {
    if (value == null || value === '') return '';
    if (value.length <= maxLength) return value;
    const ellipsis = '…';
    const charsForContent = maxLength - ellipsis.length;
    const startChars = Math.ceil(charsForContent / 2);
    const endChars = Math.floor(charsForContent / 2);
    return value.slice(0, startChars) + ellipsis + value.slice(-endChars);
  }
}
