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
import { SyncLogService } from '../../../core/services/sync-log.service';
import type { GuildSystemBgsDto } from '../../../core/models/guild-systems.model';

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
  private readonly syncLog = inject(SyncLogService);

  protected readonly inaraFactionUrl = this.guildSettings.inaraFactionPresenceUrl;
  protected readonly lastSystemsImportAt = this.guildSettings.lastSystemsImportAt;

  toggling = signal(false);
  resetting = signal(false);
  systemsMenuOpen = signal(false);
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

  protected onResetClick(): void {
    if (!confirm('Supprimer tous les systèmes de la guilde ? État propre pour un import Inara complet.')) return;
    this.resetting.set(true);
    this.api.resetSystems().subscribe({
      next: (res) => {
        this.syncLog.addLog(`Reset systèmes: ${res.deletedGuildSystems} GuildSystems, ${res.deletedControlledSystems} ControlledSystems supprimés`);
        this.guildSync.loadSystems();
      },
      error: (err) => {
        const msg = err?.error?.error ?? err?.message ?? 'Erreur';
        this.syncLog.addLog('Reset systèmes échec: ' + msg);
      },
      complete: () => this.resetting.set(false),
    });
  }

  protected onSyncSystemsClick(): void {
    this.syncLog.addLog('Clic sync systèmes → lancement');
    const url = this.guildSettings.inaraFactionPresenceUrl();
    if (!url) {
      this.syncLog.addLog('URL faction non configurée — Paramètres');
      this.syncHelpModal.show();
      return;
    }
    if (!this.inaraBridge.openWithAutoImport(url)) {
      this.syncLog.addLog('Script Inara absent — installez Tampermonkey');
      this.syncHelpModal.show();
      return;
    }
    this.syncLog.addLog('Ouverture page Inara systems');
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
    const t = this.systems().influenceThresholds ?? { critical: 10, low: 30, high: 60 };
    if (sys.influencePercent < t.critical) return 'influence-critical';
    if (sys.influencePercent < t.low) return 'influence-low';
    if (sys.influencePercent >= t.high) return 'influence-high';
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

  /** Configuration des catégories pour le template unique. Une seule catégorie par système. */
  get categoryConfig(): { key: string; label: string; systems: GuildSystemBgsDto[] }[] {
    const s = this.systems();
    return [
      { key: 'origin', label: 'Origine', systems: s.origin },
      { key: 'hq', label: 'Quartier général', systems: s.headquarter },
      { key: 'critical', label: 'Systèmes critiques', systems: s.critical },
      { key: 'others', label: 'Autres', systems: s.others },
    ];
  }
}
