// bridge-visualizer.component.ts
import { Component, inject } from '@angular/core';
import { BridgeStore } from '../../../core/services/bridge.store';
import { StarSystemDto } from '../../../core/models/models';

@Component({
  selector: 'app-bridge-visualizer',
  standalone: true,
  template: `
    <div class="viz-container">
      <div class="viz-title">▸ VISUALISATION DU PONT STELLAIRE</div>
      <div class="bridge-track">
        @for (system of store.orderedSystems(); track system.id; let last = $last) {
          <div
            class="bridge-node"
            [class.selected]="store.selectedSystem()?.id === system.id"
            (click)="store.selectSystem(system)"
            [title]="system.name"
          >
            <div
              class="node-shape"
              [class]="'type-' + system.type"
              [style.opacity]="system.status === 'FINI' ? 1 : 0.5"
            >
              {{ system.order }}
            </div>
            <div class="node-label">{{ system.name }}</div>
          </div>
          @if (!last) {
            <div class="bridge-connector"
              [class.solid]="system.status === 'FINI'">
            </div>
          }
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
}
