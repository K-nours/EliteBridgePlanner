import { Component, inject, OnInit, signal, computed } from '@angular/core';
import { GuildSystemsApiService } from '../../../core/services/guild-systems-api.service';
import { GuildSystemsSyncService } from '../../../core/services/guild-systems-sync.service';
import type { GuildSystemBgsDto } from '../../../core/models/guild-systems.model';
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
  protected readonly guildSync = inject(GuildSystemsSyncService);

  toggling = signal(false);

  panelState = this.guildSync.panelState;
  systems = this.guildSync.systems;
  lastError = this.guildSync.lastError;

  badgeSource = computed(() => {
    const s = this.panelState();
    if (s === 'cached') return 'cached';
    if (s === 'failed') return 'failed';
    if (s === 'loading') return 'seed';
    return 'seed';
  });

  badgeLabel = computed(() => {
    const s = this.panelState();
    if (s === 'loading') return 'Synchronisation...';
    if (s === 'failed') return null;
    if (s === 'cached') return null;
    return 'Non synchronisé';
  });

  badgeTooltip = computed(() => {
    const s = this.panelState();
    const err = this.lastError();
    if (s === 'loading') return 'Synchronisation en cours...';
    if (s === 'failed') return err ?? 'Une erreur est survenue';
    if (s === 'cached') return 'Données issues d\'une sync BGS';
    return 'Aucune synchronisation effectuée';
  });

  emptyMessage = computed(() => {
    const s = this.panelState();
    if (s === 'loading') return 'Chargement...';
    return 'Aucun système';
  });

  ngOnInit(): void {
    this.guildSync.loadSystems();
  }

  onSystemClick(sys: GuildSystemBgsDto): void {
    if (this.toggling() || this.panelState() === 'loading') return;
    this.toggling.set(true);
    this.api.toggleHeadquarter(sys.id).subscribe({
      next: () => this.guildSync.loadSystems(),
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

  get displayableOrigin() {
    return this.systems().origin.filter(s => !s.isFromSeed);
  }

  get displayableHeadquarter() {
    return this.systems().headquarter.filter(s => !s.isFromSeed);
  }

  get displayableCriticalSystems() {
    return this.criticalSystems.filter(s => !s.isFromSeed);
  }

  get displayableOtherSystems() {
    return this.otherSystems.filter(s => !s.isFromSeed);
  }
}
