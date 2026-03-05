import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { BridgeStore } from '../../../core/services/bridge.store';
import { SystemType, ColonizationStatus } from '../../../core/models/models';

@Component({
  selector: 'app-system-list',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './system-list.component.html',
  styleUrl: './system-list.component.scss'
})
export class SystemListComponent {
  readonly store = inject(BridgeStore);
  private readonly fb = inject(FormBuilder);

  readonly showAddForm = signal(false);

  readonly addForm = this.fb.group({
    name:             ['', [Validators.required, Validators.maxLength(200)]],
    type:             ['TABLIER' as SystemType, Validators.required],
    status:           ['PLANIFIE' as ColonizationStatus, Validators.required],
    insertAfterOrder: [0, [Validators.required, Validators.min(0)]],
    architectId:      [null as string | null]
  });

  readonly typeOptions: { value: SystemType; label: string }[] = [
    { value: 'DEBUT',   label: 'Début' },
    { value: 'PILE',    label: 'Pile' },
    { value: 'TABLIER', label: 'Tablier' },
    { value: 'FIN',     label: 'Fin' }
  ];

  readonly statusOptions: { value: ColonizationStatus; label: string }[] = [
    { value: 'PLANIFIE',     label: 'Planifié' },
    { value: 'CONSTRUCTION', label: 'En construction' },
    { value: 'FINI',         label: 'Fini' }
  ];

  toggleAddForm(): void {
    this.showAddForm.update(v => !v);
    if (this.showAddForm()) {
      this.addForm.patchValue({ insertAfterOrder: this.store.orderedSystems().length });
    }
  }

  onAdd(): void {
    if (this.addForm.invalid) return;
    const val = this.addForm.getRawValue();
    this.store.addSystem({
      name:             val.name!,
      type:             val.type!,
      status:           val.status!,
      insertAfterOrder: val.insertAfterOrder ?? 0,
      architectId:      val.architectId ?? null,
      bridgeId:         this.store.activeBridge()!.id
    });
    this.addForm.reset({ type: 'TABLIER', status: 'PLANIFIE', insertAfterOrder: 0 });
    this.showAddForm.set(false);
  }

  moveUp(id: number, currentOrder: number): void {
    if (currentOrder <= 1) return;
    this.store.reorderSystem({ id, previousSystemId: currentOrder - 1 });
  }

  moveDown(id: number, currentOrder: number): void {
    const max = this.store.orderedSystems().length;
    if (currentOrder >= max) return;
    this.store.reorderSystem({ id, previousSystemId: currentOrder + 1 });
  }

  typeLabel(type: string): string {
    return this.typeOptions.find(t => t.value === type)?.label ?? type;
  }

  statusLabel(status: string): string {
    return this.statusOptions.find(s => s.value === status)?.label ?? status;
  }
}
