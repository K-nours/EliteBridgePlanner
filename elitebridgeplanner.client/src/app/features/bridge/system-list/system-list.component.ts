import { Component, inject, signal, computed } from '@angular/core';
import type { StarSystemDto } from '../../../core/models/models';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { BridgeStore } from '../../../core/services/bridge.store';
import { SystemType, ColonizationStatus } from '../../../core/models/models';
import { TruncateMiddlePipe } from '../../../shared/pipes/truncate-middle.pipe';
import { TruncateTooltipDirective } from '../../../shared/directives/truncate-tooltip.directive';
import { CustomSelectComponent } from '../../../shared/components/custom-select/custom-select.component';

@Component({
  selector: 'app-system-list',
  standalone: true,
  imports: [ReactiveFormsModule, TruncateMiddlePipe, TruncateTooltipDirective, CustomSelectComponent],
  templateUrl: './system-list.component.html',
  styleUrl: './system-list.component.scss'
})
export class SystemListComponent {
  readonly store = inject(BridgeStore);
  private readonly fb = inject(FormBuilder);

  readonly showAddForm = signal(false);
  readonly hideOperational = signal(false);

  readonly displaySystems = computed(() => {
    const systems = this.store.orderedSystems();
    if (this.hideOperational()) {
      return systems.filter(s => s.status === 'CONSTRUCTION');
    }
    return systems;
  });

  readonly addForm = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    type: ['TABLIER' as SystemType, Validators.required],
    status: ['PLANIFIE' as ColonizationStatus, Validators.required],
    insertAtIndex: [this.store.orderedSystems().length + 1, [Validators.required, Validators.min(1)]],
    architectId: [null as string | null]
  });

  readonly typeOptions: { value: SystemType; label: string }[] = [
    { value: 'DEBUT', label: 'Départ' },
    { value: 'PILE', label: 'Pile' },
    { value: 'TABLIER', label: 'Tablier' },
    { value: 'FIN', label: 'Arrivée' }
  ];

  readonly statusOptions: { value: ColonizationStatus; label: string }[] = [
    { value: 'PLANIFIE', label: 'Planifié' },
    { value: 'CONSTRUCTION', label: 'En construction' },
    { value: 'FINI', label: 'Opérationnel' }
  ];

  toggleAddForm(): void {
    this.showAddForm.update(v => !v);
    if (this.showAddForm()) {
      this.addForm.patchValue({ insertAtIndex: this.store.orderedSystems().length + 1 });
    }
  }

  onAdd(): void {
    if (this.addForm.invalid) return;
    const val = this.addForm.getRawValue();
    this.store.addSystem({
      name: val.name!,
      type: val.type!,
      status: val.status!,
      insertAtIndex: val.insertAtIndex!,
      architectId: val.architectId ?? null,
      bridgeId: this.store.activeBridge()!.id
    });
    this.addForm.reset({ type: 'TABLIER', status: 'PLANIFIE', insertAtIndex: 1 });
    this.showAddForm.set(false);
  }

  moveUp(id: number, currentOrder: number): void {
    if (currentOrder <= 1) return;
    this.store.reorderSystem({ id, insertAtIndex: currentOrder - 1 });
  }

  moveDown(id: number, currentOrder: number): void {
    const max = this.store.orderedSystems().length;
    if (currentOrder >= max) return;
    this.store.reorderSystem({ id, insertAtIndex: currentOrder + 1 });
  }

  typeLabel(type: string): string {
    return this.typeOptions.find(t => t.value === type)?.label ?? type;
  }

  statusLabel(status: string): string {
    return this.statusOptions.find(s => s.value === status)?.label ?? status;
  }

  onSystemClick(system: StarSystemDto, event: MouseEvent): void {
    this.store.selectSystem(system, {
      shiftKey: event.shiftKey,
      systems: this.store.orderedSystems()
    });
  }
}
