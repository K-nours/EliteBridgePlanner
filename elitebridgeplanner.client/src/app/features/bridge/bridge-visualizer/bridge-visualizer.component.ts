// bridge-visualizer.component.ts
import { Component, inject, computed, signal } from '@angular/core';
import { BridgeStore } from '../../../core/services/bridge.store';
import { TruncateMiddlePipe } from '../../../shared/pipes/truncate-middle.pipe';
import { TruncateTooltipDirective } from '../../../shared/directives/truncate-tooltip.directive';
import type { StarSystemDto } from '../../../core/models/models';

const MACRO_THRESHOLD = 25;

type MacroItem =
  | { kind: 'node'; system: StarSystemDto }
  | { kind: 'link'; count: number; fromOrder: number; toOrder: number };

@Component({
  selector: 'app-bridge-visualizer',
  standalone: true,
  imports: [TruncateMiddlePipe, TruncateTooltipDirective],
  templateUrl: './bridge-visualizer.component.html',
  styleUrl: './bridge-visualizer.component.scss'
})
export class BridgeVisualizerComponent {
  readonly store = inject(BridgeStore);

  /** Vue détaillée : plage affichée (null = vue macro ou full) */
  readonly detailRange = signal<{ start: number; end: number } | null>(null);

  /** Seuil pour basculer en macro (< seuil = affichage complet) */
  readonly macroThreshold = MACRO_THRESHOLD;

  readonly orderedSystems = computed(() => this.store.orderedSystems());

  readonly lastOperationalOrder = computed(() => {
    const systems = this.orderedSystems();
    const operational = systems.filter((s) => s.status === 'FINI');
    if (operational.length === 0) return 0;
    return Math.max(...operational.map((s) => s.order));
  });

  /** Systèmes à afficher : soit détail (plage), soit tous */
  readonly displayedSystems = computed(() => {
    const systems = this.orderedSystems();
    const range = this.detailRange();
    if (!range || systems.length <= MACRO_THRESHOLD) return systems;
    return systems.filter((s) => s.order >= range.start && s.order <= range.end);
  });

  /** En vue macro : départ (toujours) + PILE + DEBUT + FIN, avec liens (N systèmes) */
  readonly macroItems = computed(() => {
    const systems = this.orderedSystems();
    if (systems.length <= MACRO_THRESHOLD) return [];
    const items: MacroItem[] = [];
    let lastNodeOrder = 0;
    for (const s of systems) {
      const showNode = s.order === 1 || s.type === 'DEBUT' || s.type === 'PILE' || s.type === 'FIN';
      if (showNode) {
        const count = s.order - lastNodeOrder - 1;
        if (count > 0) {
          items.push({
            kind: 'link',
            count,
            fromOrder: lastNodeOrder,
            toOrder: s.order
          });
        }
        items.push({ kind: 'node', system: s });
        lastNodeOrder = s.order;
      }
    }
    return items;
  });

  readonly isMacroView = computed(
    () => this.orderedSystems().length > MACRO_THRESHOLD && !this.detailRange()
  );

  readonly isFullView = computed(
    () => this.orderedSystems().length <= MACRO_THRESHOLD
  );

  readonly isDetailView = computed(() => this.detailRange() !== null);

  readonly totalCount = computed(() => this.orderedSystems().length);

  /** Segments pour la minimap : 1 segment = 1 tronçon entre 2 PILE/DEBUT/FIN */
  readonly minimapSegments = computed(() =>
    this.macroItems().filter((item): item is MacroItem & { kind: 'link' } => item.kind === 'link')
  );

  /** Zoom sur un seul segment (entre 2 piles) — inclut les 2 PILE */
  zoomIntoSegment(fromOrder: number, toOrder: number): void {
    this.detailRange.set({ start: Math.max(1, fromOrder), end: toOrder });
  }

  backToMacro(): void {
    this.detailRange.set(null);
  }

  /** Le segment affiché est-il exactement celui-ci ? (1 seul segment sélectionné) */
  isSelectedSegment(fromOrder: number, toOrder: number): boolean {
    const range = this.detailRange();
    if (!range) return false;
    return range.start === fromOrder && range.end === toOrder;
  }

  /** Position des crochets [ ] pour le segment sélectionné (0-1) */
  readonly minimapBrackets = computed(() => {
    const segments = this.minimapSegments();
    const range = this.detailRange();
    if (segments.length === 0 || !range) return null;
    const idx = segments.findIndex(
      (s) => s.fromOrder === range.start && s.toOrder === range.end
    );
    if (idx < 0) return null;
    const left = idx / segments.length;
    const right = (idx + 1) / segments.length;
    return { left, right };
  });

  selectSystem(system: StarSystemDto): void {
    this.store.selectSystem(system);
  }
}
