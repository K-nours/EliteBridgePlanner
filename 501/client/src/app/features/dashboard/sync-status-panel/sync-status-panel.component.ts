import { Component, Input, Output, EventEmitter, signal } from '@angular/core';

@Component({
  selector: 'app-sync-status-panel',
  standalone: true,
  templateUrl: './sync-status-panel.component.html',
  styleUrl: './sync-status-panel.component.scss',
})
export class SyncStatusPanelComponent {
  @Input() syncStatusCollapsed = false;
  @Input() syncLogsWithRecap: string | null = null;
  @Input() syncLogLines: string[] = [];
  @Input() mapViewMode = '';
  @Input() hasSyncLogs = false;

  @Output() toggleCollapsed = new EventEmitter<void>();
  @Output() copyLogs = new EventEmitter<void>();
  @Output() clearLogs = new EventEmitter<void>();

  readonly menuOpen = signal(false);

  isErrorLine(line: string): boolean {
    return line.toLowerCase().includes('erreur');
  }
}
