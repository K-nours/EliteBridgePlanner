import { Component, inject, effect, computed } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { BridgeStore } from '../../../core/services/bridge.store';
import { SystemType, ColonizationStatus } from '../../../core/models/models';
import { CustomSelectComponent } from '../../../shared/components/custom-select/custom-select.component';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { systemTypeOptions, systemStatusOptions } from '@app/core/enums/enums';

@Component({
  selector: 'app-system-detail',
  standalone: true,
  imports: [ReactiveFormsModule, CustomSelectComponent, TranslateModule],
  templateUrl: './system-detail.component.html',
  styleUrl: './system-detail.component.scss'
})
export class SystemDetailComponent {
  readonly store = inject(BridgeStore);
  private readonly fb = inject(FormBuilder);
  private readonly translate = inject(TranslateService);

  readonly selectedCount = computed(() => this.store.selectedSystemIds().size);

  readonly isSingleSelection = computed(() => {
    const ids = this.store.selectedSystemIds();
    if (ids.size !== 1) return null;
    const sys = this.store.selectedSystem();
    return sys ?? null;
  });

  readonly isMultiSelection = computed(() => this.store.selectedSystemIds().size > 1);

  readonly typeOptions = systemTypeOptions;
  readonly statusOptions = systemStatusOptions;

  readonly form = this.fb.group({
    name: [''],
    type: ['TABLIER' as SystemType],
    status: ['PLANIFIE' as ColonizationStatus],
    architectId: ['' as string | null]
  });

  constructor() {
    // Quand un seul système est sélectionné, mettre à jour le formulaire
    effect(() => {
      const sys = this.isSingleSelection();
      if (sys) {
        this.form.patchValue(
          {
            name: sys.name,
            type: sys.type,
            status: sys.status,
            architectId: sys.architectId ?? ''
          },
          { emitEvent: false }
        );
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
        name: val.name ?? undefined,
        type: val.type ?? undefined,
        status: val.status ?? undefined,
        architectId: val.architectId ?? ''
      }
    });
  }

  onDelete(): void {
    const sys = this.store.selectedSystem();
    if (!sys) return;
    const message = this.translate.instant('BRIDGE.SYSTEM-DETAILDELETE_CONFIRM', { name: sys.name });
    if (confirm(message)) {
      this.store.deleteSystem(sys.id);
    }
  }

  typeLabel(type: string): string {
    return this.typeOptions.find((t) => t.value === type)?.label ?? type;
  }

  statusLabel(status: string): string {
    return this.statusOptions.find((s) => s.value === status)?.label ?? status;
  }

  onBulkStatus(status: ColonizationStatus): void {
    this.store.bulkUpdateStatus(status);
  }
}
