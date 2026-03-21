import { Component, inject, OnInit, signal } from '@angular/core';
import { GuildSystemsApiService } from '../../../core/services/guild-systems-api.service';
import type { GuildSystemBgsDto, GuildSystemsResponseDto } from '../../../core/models/guild-systems.model';
import { DataSourceBadgeComponent } from '../../../shared/components/data-source-badge/data-source-badge.component';

@Component({
  selector: 'app-guild-systems-panel',
  standalone: true,
  imports: [DataSourceBadgeComponent],
  templateUrl: './guild-systems-panel.component.html',
  styleUrl: './guild-systems-panel.component.scss',
})
export class GuildSystemsPanelComponent implements OnInit {
  private readonly api = inject(GuildSystemsApiService);

  dataSource = signal<'seed' | 'cached'>('seed');
  systems = signal<GuildSystemsResponseDto>({
    origin: [],
    headquarter: [],
    others: [],
    dataSource: 'seed',
  });
  loading = signal(true);
  error = signal(false);
  toggling = signal(false);

  ngOnInit(): void {
    this.loadSystems();
  }

  loadSystems(): void {
    this.loading.set(true);
    this.api.getSystems().subscribe({
      next: (data: GuildSystemsResponseDto) => {
        this.error.set(false);
        // Jamais "live" : uniquement seed ou cached
        const ds = data?.dataSource === 'cached' ? 'cached' : 'seed';
        this.dataSource.set(ds);
        this.systems.set({
          origin: data?.origin ?? [],
          headquarter: data?.headquarter ?? [],
          others: data?.others ?? [],
          dataSource: ds,
        });
      },
      error: (err) => {
        console.error('[GuildSystemsPanel] error', err);
        this.error.set(true);
        this.systems.set({
          origin: [],
          headquarter: [],
          others: [],
          dataSource: 'seed',
        });
      },
      complete: () => this.loading.set(false),
    });
  }

  onSystemClick(sys: GuildSystemBgsDto): void {
    if (this.toggling() || this.loading()) return;
    this.toggling.set(true);
    this.api.toggleHeadquarter(sys.id).subscribe({
      next: () => this.loadSystems(),
      error: () => this.toggling.set(false),
      complete: () => this.toggling.set(false),
    });
  }

  getInfluenceClass(sys: GuildSystemBgsDto): string {
    if (sys.influencePercent < 10) return 'influence-critical';
    if (sys.influencePercent < 30) return 'influence-low';
    if (sys.influencePercent >= 60) return 'influence-high';
    return 'influence-normal';
  }

  getDeltaDisplay(delta: number): string {
    const sign = delta >= 0 ? '+' : '';
    return `${sign}${delta}%`;
  }

  get criticalSystems() {
    return this.systems().others.filter(s => s.isThreatened || s.isExpansionCandidate);
  }

  get otherSystems() {
    return this.systems().others.filter(s => !s.isThreatened && !s.isExpansionCandidate);
  }
}
