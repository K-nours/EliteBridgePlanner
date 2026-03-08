import { Component, inject, ChangeDetectorRef } from '@angular/core';
import { JsonPipe } from '@angular/common';
import { ApiExplorerService } from './api-explorer.service';

interface EdsBody {
  subType?: string;
  isLandable?: boolean;
}

interface EdsBodiesData {
  bodyCount?: number;
  bodies?: EdsBody[];
}

@Component({
  selector: 'app-api-explorer-demo',
  standalone: true,
  imports: [JsonPipe],
  templateUrl: './api-explorer-demo.component.html',
  styleUrl: './api-explorer-demo.component.scss'
})
export class ApiExplorerDemoComponent {
  private readonly apiExplorer = inject(ApiExplorerService);
  private readonly cdr = inject(ChangeDetectorRef);

  edsnResponse: unknown = null;
  edsnBodiesResponse: unknown = null;
  edsnBodiesEmpty = false;
  edsnBodiesSummary: {
    bodyCount: number;
    landable: number;
    metalRich: number;
    highMetalContent: number;
    waterWorld: number;
    earthLike: number;
  } | null = null;
  spanshResponse: unknown = null;
  spanshColonisationResponse: unknown = null;
  spanshColonisationResult: unknown = null;
  edsnError: string | null = null;
  edsnBodiesError: string | null = null;
  spanshError: string | null = null;
  spanshColonisationError: string | null = null;
  spanshColonisationResultError: string | null = null;
  loadingEdsn = false;
  loadingEdsnBodies = false;
  loadingSpansh = false;
  loadingSpanshColonisation = false;
  loadingSpanshColonisationResult = false;

  systemName = 'Mayang';
  destinationSystem = 'Colonia';
  spanshJobId = 'C1E5D9B2-1ACD-11F1-BFBB-B079545E58E4';

  expandedEdsn = true;
  expandedEdsnBodies = true;
  expandedSpansh = true;
  expandedSpanshColonisation = true;
  expandedSpanshColonisationResult = true;

  toggleExpanded(key: 'edsn' | 'edsnBodies' | 'spansh' | 'spanshColonisation' | 'spanshColonisationResult'): void {
    if (key === 'edsn') this.expandedEdsn = !this.expandedEdsn;
    if (key === 'edsnBodies') this.expandedEdsnBodies = !this.expandedEdsnBodies;
    if (key === 'spansh') this.expandedSpansh = !this.expandedSpansh;
    if (key === 'spanshColonisation') this.expandedSpanshColonisation = !this.expandedSpanshColonisation;
    if (key === 'spanshColonisationResult') this.expandedSpanshColonisationResult = !this.expandedSpanshColonisationResult;
  }

  testEdsn(): void {
    const name = this.systemName?.trim() || 'Mayang';
    this.loadingEdsn = true;
    this.edsnResponse = null;
    this.edsnError = null;
    this.apiExplorer.testEdsnSystem(name).subscribe({
      next: (data) => {
        this.edsnResponse = data;
        this.loadingEdsn = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.edsnError = err?.message ?? String(err);
        console.error('[ApiExplorerDemo] EDSM error:', err);
        this.loadingEdsn = false;
        this.cdr.detectChanges();
      }
    });
  }

  testEdsnBodies(): void {
    const name = this.systemName?.trim() || 'Mayang';
    this.loadingEdsnBodies = true;
    this.edsnBodiesResponse = null;
    this.edsnBodiesSummary = null;
    this.edsnBodiesEmpty = false;
    this.edsnBodiesError = null;
    this.apiExplorer.testEdsnBodies(name).subscribe({
      next: (data) => {
        this.edsnBodiesResponse = data;
        this.edsnBodiesSummary = this.computeBodiesSummary(data as EdsBodiesData);
        this.edsnBodiesEmpty = this.isBodiesResponseEmpty(data as EdsBodiesData);
        this.loadingEdsnBodies = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.edsnBodiesError = err?.message ?? String(err);
        console.error('[ApiExplorerDemo] EDSM bodies error:', err);
        this.loadingEdsnBodies = false;
        this.cdr.detectChanges();
      }
    });
  }

  private isBodiesResponseEmpty(data: EdsBodiesData | unknown): boolean {
    if (data == null) return true;
    if (Array.isArray(data) && data.length === 0) return true;
    const d = data as EdsBodiesData;
    if (d.bodyCount === 0) return true;
    const bodies = d.bodies ?? [];
    return bodies.length === 0;
  }

  private computeBodiesSummary(data: EdsBodiesData): {
    bodyCount: number;
    landable: number;
    metalRich: number;
    highMetalContent: number;
    waterWorld: number;
    earthLike: number;
  } {
    const bodies = data?.bodies ?? [];
    return {
      bodyCount: data?.bodyCount ?? bodies.length,
      landable: bodies.filter((b) => b.isLandable === true).length,
      metalRich: bodies.filter((b) => (b.subType ?? '').includes('Metal-rich')).length,
      highMetalContent: bodies.filter((b) => (b.subType ?? '').includes('High metal content')).length,
      waterWorld: bodies.filter((b) => (b.subType ?? '').includes('Water world')).length,
      earthLike: bodies.filter((b) => (b.subType ?? '').includes('Earth-like world')).length
    };
  }

  testSpanshColonisationRoute(): void {
    const source = this.systemName?.trim() || 'Mayang';
    const destination = this.destinationSystem?.trim() || 'Colonia';
    this.loadingSpanshColonisation = true;
    this.spanshColonisationResponse = null;
    this.spanshColonisationResult = null;
    this.spanshColonisationError = null;
    this.spanshColonisationResultError = null;
    this.apiExplorer.testSpanshColonisationRoute(source, destination).subscribe({
      next: (data) => {
        this.spanshColonisationResponse = data;
        this.loadingSpanshColonisation = false;
        this.cdr.detectChanges();

        const jobId = (data as { job?: string })?.job;
        if (jobId) {
          setTimeout(() => {
            this.loadingSpanshColonisationResult = true;
            this.cdr.detectChanges();
            this.apiExplorer.getSpanshColonisationResult(jobId).subscribe({
              next: (result) => {
                this.spanshColonisationResult = result;
                this.loadingSpanshColonisationResult = false;
                this.cdr.detectChanges();
              },
              error: (err) => {
                this.spanshColonisationResultError = err?.message ?? String(err);
                console.error('[ApiExplorerDemo] Spansh Colonisation result error:', err);
                this.loadingSpanshColonisationResult = false;
                this.cdr.detectChanges();
              }
            });
          }, 2000);
        }
      },
      error: (err) => {
        this.spanshColonisationError = err?.message ?? String(err);
        console.error('[ApiExplorerDemo] Spansh Colonisation error:', err);
        this.loadingSpanshColonisation = false;
        this.cdr.detectChanges();
      }
    });
  }

  fetchSpanshColonisationResult(): void {
    const jobId = this.spanshJobId?.trim();
    if (!jobId) return;
    this.loadingSpanshColonisationResult = true;
    this.spanshColonisationResult = null;
    this.spanshColonisationResultError = null;
    this.cdr.detectChanges();
    this.apiExplorer.getSpanshColonisationResult(jobId).subscribe({
      next: (result) => {
        this.spanshColonisationResult = result;
        this.loadingSpanshColonisationResult = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.spanshColonisationResultError = err?.message ?? String(err);
        console.error('[ApiExplorerDemo] Spansh Colonisation result error:', err);
        this.loadingSpanshColonisationResult = false;
        this.cdr.detectChanges();
      }
    });
  }

  testSpansh(): void {
    this.loadingSpansh = true;
    this.spanshResponse = null;
    this.spanshError = null;
    this.apiExplorer.testSpanshRoute('Sol').subscribe({
      next: (data) => {
        this.spanshResponse = data;
        this.loadingSpansh = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.spanshError = err?.message ?? String(err);
        console.error('[ApiExplorerDemo] Spansh error:', err);
        this.loadingSpansh = false;
        this.cdr.detectChanges();
      }
    });
  }
}
