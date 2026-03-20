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

  dataSource = signal<'live' | 'seed' | 'mock'>('mock');
  systems = signal<GuildSystemsResponseDto>({ origin: [], headquarter: [], others: [] });
  loading = signal(true);

  ngOnInit(): void {
    this.api.getSystems().subscribe({
      next: (data: GuildSystemsResponseDto) => {
        const hasData = data.origin.length > 0 || data.headquarter.length > 0 || data.others.length > 0;
        this.dataSource.set(hasData ? 'live' : 'seed');
        this.systems.set(data);
      },
      error: () => {
        this.dataSource.set('mock');
        this.systems.set({ origin: [], headquarter: [], others: [] });
      },
      complete: () => this.loading.set(false),
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
