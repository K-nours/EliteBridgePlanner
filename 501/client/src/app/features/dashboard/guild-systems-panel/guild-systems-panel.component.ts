/**
 * Feature archived – no reliable external data source for Faction → Systems → Influence %.
 * Panneau conservé avec marquage "Feature en pause". Voir docs/GUILD-SYSTEMS.md.
 */
import { Component, inject, OnInit, signal, computed } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { GuildSystemsApiService } from '../../../core/services/guild-systems-api.service';
import { GuildSystemsSyncService } from '../../../core/services/guild-systems-sync.service';
import { GuildSettingsService } from '../../../core/services/guild-settings.service';
import { InaraSyncBridgeService } from '../../../core/services/inara-sync-bridge.service';
import { SyncHelpModalService } from '../../../core/services/sync-help-modal.service';
import type { GuildSystemBgsDto } from '../../../core/models/guild-systems.model';

/** Seuils influence — voir docs/GUILD-SYSTEMS.md */
const INFLUENCE_CRITICAL = 10;  // < 10% = rouge vif
const INFLUENCE_LOW = 30;       // < 30% = rouge
const INFLUENCE_HIGH = 60;      // >= 60% = vert

@Component({
  selector: 'app-guild-systems-panel',
  standalone: true,
  imports: [NgTemplateOutlet],
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
  /** ID du système dont le nom vient d'être copié (feedback tooltip "Copié") */
  copiedSystemId = signal<number | null>(null);

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
    if (!this.inaraBridge.openWithAutoImport(url)) {
      this.syncHelpModal.show();
    }
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
    if (sys.influencePercent < INFLUENCE_CRITICAL) return 'influence-critical';
    if (sys.influencePercent < INFLUENCE_LOW) return 'influence-low';
    if (sys.influencePercent >= INFLUENCE_HIGH) return 'influence-high';
    return 'influence-normal';
  }

  getDeltaDisplay(delta: number): string {
    const sign = delta >= 0 ? '+' : '';
    return `${sign}${delta}%`;
  }

  /** Copie le nom du système dans le presse-papiers. Retourne true si succès. */
  async copySystemName(event: Event, name: string, id: number): Promise<void> {
    event.stopPropagation();
    try {
      await navigator.clipboard.writeText(name);
      this.copiedSystemId.set(id);
      setTimeout(() => this.copiedSystemId.set(null), 1500);
    } catch {
      // Fallback anciens navigateurs
      try {
        const ta = document.createElement('textarea');
        ta.value = name;
        ta.style.position = 'fixed';
        ta.style.left = '-9999px';
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
        this.copiedSystemId.set(id);
        setTimeout(() => this.copiedSystemId.set(null), 1500);
      } catch {
        /* ignore */
      }
    }
  }

  copyTooltip(sysId: number): string {
    return this.copiedSystemId() === sysId ? 'Copié' : 'Copier';
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

  /** Configuration des catégories pour le template unique */
  get categoryConfig(): { key: string; label: string; systems: GuildSystemBgsDto[] }[] {
    return [
      { key: 'origin', label: 'Origine', systems: this.displayableOrigin },
      { key: 'hq', label: 'Quartier général', systems: this.displayableHeadquarter },
      { key: 'critical', label: 'Systèmes critiques', systems: this.displayableCriticalSystems },
      { key: 'others', label: 'Autres', systems: this.displayableOtherSystems },
    ];
  }
}
