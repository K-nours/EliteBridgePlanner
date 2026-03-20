import {
  Directive,
  ElementRef,
  Input,
  HostListener,
  AfterViewInit,
  OnDestroy,
  OnChanges,
  SimpleChanges,
  Inject,
} from '@angular/core';
import { DOCUMENT } from '@angular/common';

/**
 * Affiche un tooltip au survol.
 * Utilise position: fixed pour s'afficher au-dessus de tout le contenu.
 * Même techno que le projet EliteBridgePlanner.
 */
@Directive({
  selector: '[truncateTooltip]',
  standalone: true,
})
export class TruncateTooltipDirective implements AfterViewInit, OnDestroy, OnChanges {
  @Input() truncateTooltip = '';
  /** Quand true, affiche le tooltip même sans overflow CSS */
  @Input() truncateTooltipForce = false;
  /** Quand true, positionne le tooltip au-dessus de l'élément */
  @Input() truncateTooltipAbove = false;

  private resizeObserver?: ResizeObserver;
  private tooltipEl: HTMLElement | null = null;
  private isTruncated = false;

  constructor(
    private el: ElementRef<HTMLElement>,
    @Inject(DOCUMENT) private doc: Document
  ) {}

  ngAfterViewInit(): void {
    this.checkTruncation();
    this.resizeObserver = new ResizeObserver(() => this.checkTruncation());
    this.resizeObserver.observe(this.el.nativeElement);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['truncateTooltip'] || changes['truncateTooltipForce'] || changes['truncateTooltipAbove']) {
      this.checkTruncation();
    }
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.hideTooltip();
  }

  @HostListener('mouseenter')
  onMouseEnter(): void {
    if (this.isTruncated && this.truncateTooltip) {
      this.showTooltip();
    }
  }

  @HostListener('mouseleave')
  onMouseLeave(): void {
    this.hideTooltip();
  }

  private checkTruncation(): void {
    requestAnimationFrame(() => {
      const el = this.el.nativeElement;
      this.isTruncated = this.truncateTooltipForce || el.scrollWidth > el.clientWidth;

      if (this.isTruncated && this.truncateTooltip) {
        el.classList.add('card-has-tooltip');
      } else {
        el.classList.remove('card-has-tooltip');
      }
    });
  }

  private showTooltip(): void {
    this.hideTooltip();
    const rect = this.el.nativeElement.getBoundingClientRect();
    const tooltip = this.doc.createElement('div');
    tooltip.className = 'eb-tooltip-floating';
    tooltip.textContent = this.truncateTooltip;
    tooltip.style.left = `${rect.left + rect.width / 2}px`;
    if (this.truncateTooltipAbove) {
      tooltip.style.top = `${rect.top - 6}px`;
      tooltip.style.transform = 'translate(-50%, -100%)';
    } else {
      tooltip.style.top = `${rect.bottom + 6}px`;
      tooltip.style.transform = 'translateX(-50%)';
    }
    this.doc.body.appendChild(tooltip);
    this.tooltipEl = tooltip;
    requestAnimationFrame(() => tooltip.classList.add('visible'));
  }

  private hideTooltip(): void {
    if (this.tooltipEl?.parentNode) {
      this.tooltipEl.parentNode.removeChild(this.tooltipEl);
    }
    this.tooltipEl = null;
  }
}
