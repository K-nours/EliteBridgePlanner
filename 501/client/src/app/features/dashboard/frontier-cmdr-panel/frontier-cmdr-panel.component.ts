import { Component, ElementRef, EventEmitter, Input, Output, ViewChild, signal } from '@angular/core';
import type { FrontierProfileDto } from '../../../core/models/dashboard.model';
import type { FrontierJournalUnifiedSyncStatusDto } from '../../../core/services/frontier-journal-api.service';

@Component({
  selector: 'app-frontier-cmdr-panel',
  standalone: true,
  imports: [],
  templateUrl: './frontier-cmdr-panel.component.html',
  styleUrl: './frontier-cmdr-panel.component.scss',
})
export class FrontierCmdrPanelComponent {
  @ViewChild('journalImportInput') journalImportInput?: ElementRef<HTMLInputElement>;

  @Input({ required: true }) frontierProfile!: FrontierProfileDto;
  @Input() connectedCmdrAvatar: string | null = null;
  @Input() journalUnifiedRunning = false;
  @Input() journalUnifiedStatus: FrontierJournalUnifiedSyncStatusDto | null = null;
  @Input() journalFrontierTooltip = '';
  @Input() rareCommodityHasAlert = false;
  @Input() rareCommodityAlertUrl = '';
  @Input() rareCommodityAlertTooltip = '';
  @Input() rareCommodityIsReady = false;
  @Input() rareCommodityOkTooltip = '';

  @Output() journalExport = new EventEmitter<void>();
  @Output() journalImportReplace = new EventEmitter<void>();
  @Output() syncJournal = new EventEmitter<void>();
  @Output() connectFrontierForJournal = new EventEmitter<void>();
  @Output() journalFileSelected = new EventEmitter<Event>();

  protected readonly boxAvatarError = signal(false);
  protected readonly cmdrJournalMenuOpen = signal(false);

  protected onImportReplaceClick(): void {
    this.journalImportReplace.emit();
    queueMicrotask(() => this.journalImportInput?.nativeElement?.click());
  }

  protected onFileSelected(event: Event): void {
    this.journalFileSelected.emit(event);
  }
}
