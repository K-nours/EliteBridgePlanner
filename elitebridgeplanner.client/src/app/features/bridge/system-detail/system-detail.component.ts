import { Component, inject, effect, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { BridgeStore } from '../../../core/services/bridge.store';
import { SystemType, ColonizationStatus } from '../../../core/models/models';

@Component({
  selector: 'app-system-detail',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './system-detail.component.html',
  styleUrl: './system-detail.component.scss'
})
export class SystemDetailComponent {
  readonly store = inject(BridgeStore);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.group({
    name:        [''],
    type:        ['TABLIER' as SystemType],
    status:      ['PLANIFIE' as ColonizationStatus],
    architectId: ['' as string | null]
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
    { value: 'FINI',         label: 'Opérationnel' }
  ];

  constructor() {
    // Quand le système sélectionné change, mettre à jour le formulaire
    effect(() => {
      const sys = this.store.selectedSystem();
      if (sys) {
        this.form.patchValue({
          name:        sys.name,
          type:        sys.type,
          status:      sys.status,
          architectId: sys.architectId ?? ''
        }, { emitEvent: false });
      }
    });
  }

  onSave(): void {
    const sys = this.store.selectedSystem();
    if (!sys) return;
    const val = this.form.getRawValue();
    this.store.updateSystem({
      id: sys.id,
      request: {
        name:        val.name ?? undefined,
        type:        val.type ?? undefined,
        status:      val.status ?? undefined,
        architectId: val.architectId ?? ''
      }
    });
  }

  onDelete(): void {
    const sys = this.store.selectedSystem();
    if (!sys) return;
    if (confirm(`Supprimer "${sys.name}" ?`)) {
      this.store.deleteSystem(sys.id);
    }
  }

  typeLabel(type: string): string {
    return this.typeOptions.find(t => t.value === type)?.label ?? type;
  }

  statusLabel(status: string): string {
    return this.statusOptions.find(s => s.value === status)?.label ?? status;
  }
}
