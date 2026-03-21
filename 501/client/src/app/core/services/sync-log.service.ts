import { Injectable, signal, computed } from '@angular/core';

/** Service partagé pour les logs de synchronisation (commanders, BGS, etc.). */
@Injectable({ providedIn: 'root' })
export class SyncLogService {
  readonly logs = signal<string[]>([]);
  readonly logsText = computed(() => this.logs().join('\n') || '(aucun log)');

  addLog(msg: string): void {
    const ts = new Date().toISOString().slice(11, 23);
    this.logs.update(list => [...list, `[${ts}] ${msg}`]);
  }

  clearLogs(): void {
    this.logs.set([]);
  }
}
