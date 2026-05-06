import { Component, input, computed } from '@angular/core';
import { DataSourceType, DATA_SOURCE_LABELS } from '../../../core/models/data-source.model';

@Component({
  selector: 'app-data-source-badge',
  standalone: true,
  template: `
    @if (show()) {
      <span class="data-source-badge" [class]="'data-source-badge--' + source()" [title]="tooltip()">
        {{ label() }}
      </span>
    }
  `,
  styles: [`
    .data-source-badge {
      font-family: 'Orbitron', sans-serif;
      font-size: 0.6rem;
      padding: 0.1rem 0.35rem;
      border-radius: 2px;
      opacity: 0.85;
    }
    .data-source-badge--live { background: rgba(0, 255, 136, 0.2); color: #00ff88; }
    .data-source-badge--cached { background: rgba(0, 212, 255, 0.2); color: #00d4ff; }
    .data-source-badge--seed { background: rgba(255, 194, 54, 0.15); color: #ffc236; }
    .data-source-badge--mock { background: rgba(128, 128, 128, 0.2); color: #aaa; }
    .data-source-badge--failed { background: rgba(255, 100, 100, 0.2); color: #ff6b6b; }
  `],
})
export class DataSourceBadgeComponent {
  source = input.required<DataSourceType>();
  show = input<boolean>(true);
  customLabel = input<string | null>(null);
  tooltip = input<string>('');

  label = computed(() => this.customLabel() ?? DATA_SOURCE_LABELS[this.source()]);
}
