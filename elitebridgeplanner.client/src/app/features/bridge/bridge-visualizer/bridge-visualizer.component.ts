// bridge-visualizer.component.ts
import { Component, inject, computed } from '@angular/core';
import { BridgeStore } from '../../../core/services/bridge.store';
import { TruncateMiddlePipe } from '../../../shared/pipes/truncate-middle.pipe';
import { TruncateTooltipDirective } from '../../../shared/directives/truncate-tooltip.directive';

@Component({
  selector: 'app-bridge-visualizer',
  standalone: true,
  imports: [TruncateMiddlePipe, TruncateTooltipDirective],
  template: `
    <div class="viz-container">
      <div class="viz-title">▸ VISUALISATION DU PONT STELLAIRE</div>
      <div class="bridge-track">
        @for (system of store.orderedSystems(); track system.id; let i = $index) {
          @if (i > 0) {
            <div
              class="line-segment"
              [class.segment-green]="system.order <= lastOperationalOrder()"
              [class.segment-normal]="system.order > lastOperationalOrder()"
            ></div>
          }
          <div
            [class]="'bridge-node type-' + system.type"
            [class.selected]="store.selectedSystem()?.id === system.id"
            (click)="store.selectSystem(system)"
          >
            <div
              [class]="'node-shape type-' + system.type"
            >
              {{ system.order }}
            </div>
            <div
              class="node-label"
              [truncateTooltip]="system.name"
              [truncateTooltipForce]="system.name.length > 14"
              [truncateTooltipAbove]="true"
            >{{ system.name | truncateMiddle:14 }}</div>
          </div>
        }
        @if (store.orderedSystems().length === 0) {
          <span class="empty-viz">Aucun système — ajoutez-en un pour commencer</span>
        }
      </div>
    </div>
  `,
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
