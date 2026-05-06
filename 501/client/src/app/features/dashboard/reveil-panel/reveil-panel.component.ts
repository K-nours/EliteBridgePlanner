import { Component, EventEmitter, Input, Output } from '@angular/core';

export interface ReveilSystemVm {
  id: number;
  name: string;
  inaraUrl: string | null;
  ageDays: number | null;
}

@Component({
  selector: 'app-reveil-panel',
  standalone: true,
  imports: [],
  templateUrl: './reveil-panel.component.html',
  styleUrl: './reveil-panel.component.scss',
})
export class ReveilPanelComponent {
  @Input() reveilSystems: ReveilSystemVm[] = [];
  @Input() copiedReveilSystemId: number | null = null;

  @Output() openInara = new EventEmitter<{ inaraUrl: string | null; name: string }>();
  @Output() copySystemName = new EventEmitter<{ event: MouseEvent; name: string; id: number }>();
}
