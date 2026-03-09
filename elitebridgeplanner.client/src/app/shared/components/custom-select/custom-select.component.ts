import {
  Component,
  Input,
  HostBinding,
  HostListener,
  ElementRef,
  ViewChild,
  inject,
  signal,
  computed,
  afterNextRender,
  Injector,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export interface CustomSelectOption<T = string> {
  value: T;
  label: string;
}

@Component({
  selector: 'app-custom-select',
  standalone: true,
  template: `
    <div class="custom-select" [class.open]="isOpen()">
      <button
        #triggerRef
        type="button"
        class="select-trigger"
        [id]="triggerId"
        [attr.aria-haspopup]="'listbox'"
        [attr.aria-expanded]="isOpen()"
        [attr.aria-labelledby]="labelId || null"
        [attr.aria-label]="label || null"
        [attr.aria-activedescendant]="isOpen() && highlightedIndex() >= 0 ? optionId(highlightedIndex()) : null"
        (click)="toggle()"
        (keydown)="onTriggerKeydown($event)"
      >
        <span class="select-value">{{ displayedLabel() }}</span>
        <span class="select-arrow" aria-hidden="true">▼</span>
      </button>
      @if (isOpen()) {
        <ul
          class="select-dropdown"
          role="listbox"
          [attr.aria-labelledby]="labelId || null"
          [attr.aria-label]="label || null"
          [attr.aria-activedescendant]="highlightedIndex() >= 0 ? optionId(highlightedIndex()) : null"
        >
          @for (opt of options; track opt.value; let i = $index) {
            <li
              [id]="optionId(i)"
              role="option"
              [attr.aria-selected]="value() === opt.value"
              [class.selected]="value() === opt.value"
              [class.highlighted]="highlightedIndex() === i"
              (click)="select(opt)"
              (mouseenter)="highlightedIndex.set(i)"
            >
              {{ opt.label }}
            </li>
          }
        </ul>
      }
    </div>
  `,
  styleUrl: './custom-select.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: CustomSelectComponent,
      multi: true,
    },
  ],
})
export class CustomSelectComponent<T = string> implements ControlValueAccessor {
  @Input() options: CustomSelectOption<T>[] = [];
  @Input() placeholder = 'Sélectionner…';
  @Input() labelId = '';
  @Input() label = '';

  @HostBinding('attr.role') role = 'combobox';
  @HostBinding('attr.aria-disabled') get ariaDisabled() {
    return this.disabled ? 'true' : null;
  }

  private readonly el = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);
  @ViewChild('triggerRef') triggerRef?: ElementRef<HTMLButtonElement>;

  value = signal<T | null>(null);
  disabled = false;
  isOpen = signal(false);
  highlightedIndex = signal(-1);

  readonly triggerId = `custom-select-trigger-${Math.random().toString(36).slice(2, 9)}`;

  displayedLabel = computed(() => {
    const v = this.value();
    if (v == null) return this.placeholder;
    const opt = this.options.find(o => o.value === v);
    return opt?.label ?? this.placeholder;
  });

  optionId(index: number): string {
    return `${this.triggerId}-option-${index}`;
  }

  private onChange: (value: T | null) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: T | null): void {
    this.value.set(value);
    this.highlightedIndex.set(
      value != null ? this.options.findIndex(o => o.value === value) : -1
    );
  }

  registerOnChange(fn: (value: T | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }

  toggle(): void {
    if (this.disabled) return;
    this.isOpen.update(v => !v);
    if (this.isOpen()) {
      const v = this.value();
      const idx = v != null
        ? this.options.findIndex(o => o.value === v)
        : 0;
      this.highlightedIndex.set(Math.max(0, idx));
    } else {
      this.onTouched();
    }
  }

  close(returnFocusToTrigger = false): void {
    this.isOpen.set(false);
    this.highlightedIndex.set(-1);
    this.onTouched();
    if (returnFocusToTrigger) {
      afterNextRender(() => this.triggerRef?.nativeElement?.focus(), { injector: this.injector });
    }
  }

  select(opt: CustomSelectOption<T>): void {
    this.value.set(opt.value);
    this.onChange(opt.value);
    this.close(true);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.isOpen() && !this.el.nativeElement.contains(event.target as Node)) {
      this.close(false);
    }
  }

  onTriggerKeydown(event: KeyboardEvent): void {
    if (this.disabled) return;
    switch (event.key) {
      case 'Enter':
      case ' ':
        event.preventDefault();
        if (this.isOpen()) {
          const idx = this.highlightedIndex();
          if (idx >= 0 && this.options[idx]) {
            this.select(this.options[idx]);
          }
        } else {
          this.toggle();
        }
        break;
      case 'ArrowDown':
        event.preventDefault();
        if (!this.isOpen()) {
          this.isOpen.set(true);
          this.highlightedIndex.set(0);
        } else {
          this.highlightedIndex.update(i =>
            Math.min(i + 1, this.options.length - 1)
          );
        }
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (!this.isOpen()) {
          this.isOpen.set(true);
          this.highlightedIndex.set(this.options.length - 1);
        } else {
          this.highlightedIndex.update(i => Math.max(i - 1, 0));
        }
        break;
      case 'Escape':
        event.preventDefault();
        this.close(true);
        break;
      case 'Home':
        if (this.isOpen()) {
          event.preventDefault();
          this.highlightedIndex.set(0);
        }
        break;
      case 'End':
        if (this.isOpen()) {
          event.preventDefault();
          this.highlightedIndex.set(this.options.length - 1);
        }
        break;
    }
  }
}
