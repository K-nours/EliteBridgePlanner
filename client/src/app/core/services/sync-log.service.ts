import { Injectable, signal, computed } from '@angular/core';

/** Service partagé pour les logs de synchronisation (commanders, BGS, etc.). */
@Injectable({ providedIn: 'root' })
export class SyncLogService {
  readonly logs = signal<string[]>([]);
  readonly logsText = computed(() => this.logs().join('\n') || '(aucun log)');

  /** Nombre max de lignes pour limiter la croissance en session. */
  private static readonly MAX_LOGS = 200;

  addLog(msg: string): void {
    const ts = new Date().toISOString().slice(11, 23);
    this.logs.update(list => {
      const next = [...list, `[${ts}] ${msg}`];
      return next.length > SyncLogService.MAX_LOGS ? next.slice(-SyncLogService.MAX_LOGS) : next;
    });
  }

  clearLogs(): void {
    this.logs.set([]);
  }
}
