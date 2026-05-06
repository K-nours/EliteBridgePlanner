import { Component, Input, signal } from '@angular/core';
import type { FrontierProfileDto } from '../../../core/models/dashboard.model';

@Component({
  selector: 'app-frontier-cmdr-panel',
  standalone: true,
  imports: [],
  templateUrl: './frontier-cmdr-panel.component.html',
  styleUrl: './frontier-cmdr-panel.component.scss',
})
export class FrontierCmdrPanelComponent {
  @Input({ required: true }) frontierProfile!: FrontierProfileDto;
  @Input() connectedCmdrAvatar: string | null = null;

  protected readonly boxAvatarError = signal(false);
}
