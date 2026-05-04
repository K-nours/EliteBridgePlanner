import { Component, inject } from '@angular/core';
import { BridgeStore } from '../../../core/services/bridge.store';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  selector: 'app-stations-panel',
  standalone: true,
  imports: [TranslateModule],
  templateUrl: './stations-panel.component.html',
  styleUrl: './stations-panel.component.scss'
})
export class StationsPanelComponent {
  readonly store = inject(BridgeStore);
}
