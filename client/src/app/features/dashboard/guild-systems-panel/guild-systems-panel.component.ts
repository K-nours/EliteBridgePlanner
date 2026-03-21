/**
 * Feature archived – no reliable external data source for Faction → Systems → Influence %.
 * Panneau conservé avec marquage "Feature en pause". Voir docs/GUILD-SYSTEMS.md.
 */
import { Component, inject, OnInit, signal, computed } from '@angular/core';
import { GuildSystemsApiService } from '../../../core/services/guild-systems-api.service';
import { GuildSystemsSyncService } from '../../../core/services/guild-systems-sync.service';
import { GuildSettingsService } from '../../../core/services/guild-settings.service';
import { InaraSyncBridgeService } from '../../../core/services/inara-sync-bridge.service';
import { SyncHelpModalService } from '../../../core/services/sync-help-modal.service';
import type { GuildSystemBgsDto } from '../../../core/models/guild-systems.model';
@Component({
  selector: 'app-guild-systems-panel',
  standalone: true,
  templateUrl: './guild-systems-panel.component.html',
  styleUrl: './guild-systems-panel.component.scss',
})
export class GuildSystemsPanelComponent implements OnInit {
  private readonly api = inject(GuildSystemsApiService);
  protected readonly guildSync = inject(GuildSystemsSyncService);
  protected readonly guildSettings = inject(GuildSettingsService);
  private readonly inaraBridge = inject(InaraSyncBridgeService);
  private readonly syncHelpModal = inject(SyncHelpModalService);

  protected readonly inaraFactionUrl = this.guildSettings.inaraFactionPresenceUrl;
  protected readonly lastSystemsImportAt = this.guildSettings.lastSystemsImportAt;

  toggling = signal(false);

  panelState = this.guildSync.panelState;
  systems = this.guildSync.systems;
  lastError = this.guildSync.lastError;

  emptyMessage = computed(() => {
    const s = this.panelState();
    if (s === 'loading') return 'Chargement...';
    return 'Aucun système';
  });

  ngOnInit(): void {
    this.guildSync.loadSystems();
  }

  protected formatLastSync(iso: string): string {
    const d = new Date(iso);
    return d.toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'medium' });
  }

  protected onSyncSystemsClick(): void {
    const url = this.guildSettings.inaraFactionPresenceUrl();
    if (!url) return;
    if (!this.inaraBridge.checkNow()) {
      this.syncHelpModal.show();
      return;
    }
    window.open(url, '_blank', 'noopener,noreferrer');
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

  /** Base interne = source primaire. Tous les systèmes seedés sont affichés. */
  get displayableOrigin() {
    return this.systems().origin;
  }

  get displayableHeadquarter() {
    return this.systems().headquarter;
  }

  get displayableCriticalSystems() {
    return this.criticalSystems;
  }

  get displayableOtherSystems() {
    return this.otherSystems;
  }
}
