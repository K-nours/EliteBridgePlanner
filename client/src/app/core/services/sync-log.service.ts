import { Injectable, signal, computed } from '@angular/core';
import { CHANTIERS_INSPECT_LOG_MARKER } from '../utils/chantiers-inspect-sync-log';

/** Service partagé pour les logs de synchronisation (commanders, BGS, etc.). */
@Injectable({ providedIn: 'root' })
export class SyncLogService {
  readonly logs = signal<string[]>([]);
  readonly logsText = computed(() => this.logs().join('\n') || '(aucun log)');

  /** Nombre max d’entrées (chaque entrée est déjà bornée en taille). */
  private static readonly MAX_LOGS = 200;
  private static readonly MAX_CHARS_PER_ENTRY = 4000;
  private static readonly MAX_LINES_PER_ENTRY = 40;

  /** Remplace l’entrée « chantiers inspect » précédente — une seule ligne de debug par action. */
  setChantiersInspectLog(msg: string): void {
    const sanitized = SyncLogService.sanitizeLogMessage(msg);
    const ts = new Date().toISOString().slice(11, 23);
    this.logs.update(list => {
      const filtered = list.filter(l => !l.includes(CHANTIERS_INSPECT_LOG_MARKER));
      const next = [...filtered, `[${ts}] ${sanitized}`];
      return next.length > SyncLogService.MAX_LOGS ? next.slice(-SyncLogService.MAX_LOGS) : next;
    });
  }

  addLog(msg: string): void {
    const sanitized = SyncLogService.sanitizeLogMessage(msg);
    const ts = new Date().toISOString().slice(11, 23);
    this.logs.update(list => {
      const next = [...list, `[${ts}] ${sanitized}`];
      return next.length > SyncLogService.MAX_LOGS ? next.slice(-SyncLogService.MAX_LOGS) : next;
    });
  }

  private static sanitizeLogMessage(msg: string): string {
    let lines = msg.split('\n');
    if (lines.length > SyncLogService.MAX_LINES_PER_ENTRY) {
      lines = [...lines.slice(0, SyncLogService.MAX_LINES_PER_ENTRY), '[tronqué : trop de lignes]'];
    }
    let out = lines.join('\n');
    if (out.length > SyncLogService.MAX_CHARS_PER_ENTRY) {
      out = out.slice(0, SyncLogService.MAX_CHARS_PER_ENTRY) + '\n[tronqué]';
    }
    return out;
  }

  clearLogs(): void {
    this.logs.set([]);
  }
}
