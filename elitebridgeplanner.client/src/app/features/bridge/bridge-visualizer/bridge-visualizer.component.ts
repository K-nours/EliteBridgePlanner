// bridge-visualizer.component.ts
import { Component, inject, computed } from '@angular/core';
import { BridgeStore } from '../../../core/services/bridge.store';
import { TruncateMiddlePipe } from '../../../shared/pipes/truncate-middle.pipe';
import { TruncateTooltipDirective } from '../../../shared/directives/truncate-tooltip.directive';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  selector: 'app-bridge-visualizer',
  standalone: true,
  imports: [TruncateMiddlePipe, TruncateTooltipDirective, TranslateModule],
  templateUrl: './bridge-visualizer.component.html',
  styleUrl: './bridge-visualizer.component.scss'
})
export class BridgeVisualizerComponent {
  readonly store = inject(BridgeStore);

  /** Ordre du dernier système opérationnel (FINI) — la ligne verte s'arrête là */
  readonly lastOperationalOrder = computed(() => {
    const systems = this.store.orderedSystems();
    const operational = systems.filter(s => s.status === 'FINI');
    if (operational.length === 0) return 0;
    return Math.max(...operational.map(s => s.order));
  });
}