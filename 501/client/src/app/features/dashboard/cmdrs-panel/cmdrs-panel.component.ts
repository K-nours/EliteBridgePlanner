import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import type { CommandersResponseDto } from '../../../core/models/commanders.model';

@Component({
  selector: 'app-cmdrs-panel',
  standalone: true,
  imports: [],
  templateUrl: './cmdrs-panel.component.html',
  styleUrl: './cmdrs-panel.component.scss',
})
export class CmdrsPanelComponent {
  @Input() commandersData: CommandersResponseDto | null = null;
  @Input() currentCmdrName: string | null = null;
  @Input() inaraSquadronUrl: string | null = null;
  @Input() syncAvatarsRosterTooltip = '';
  @Input() syncCmdrsTooltip = '';
  @Input() avatarDefaultFallbackUrl: string | null = null;

  @Output() syncAvatars = new EventEmitter<void>();
  @Output() syncCmdrs = new EventEmitter<void>();

  protected readonly CMDRS_PER_PAGE = 8;
  protected readonly Math = Math;

  protected readonly cmdrPage = signal(0);
  protected readonly cmdrsMenuOpen = signal(false);
  protected readonly cmdrAvatarError = signal<Set<string>>(new Set());

  protected addCmdrAvatarError(name: string): void {
    this.cmdrAvatarError.update((s) => new Set(s).add(name));
  }

  protected isCurrent(name: string): boolean {
    return (this.currentCmdrName ?? '').trim().toLowerCase() === name.trim().toLowerCase();
  }
}
