/**
 * Feature archived – no reliable external data source for Faction → Systems → Influence %.
 * Panneau conservé avec marquage "Feature en pause". Voir docs/GUILD-SYSTEMS.md.
 */
import { Component, inject, OnInit, signal, computed, effect, output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { GuildSystemsApiService } from '../../../core/services/guild-systems-api.service';
import { GuildSystemsSyncService } from '../../../core/services/guild-systems-sync.service';
import { GuildSettingsService } from '../../../core/services/guild-settings.service';
import { InaraSyncBridgeService } from '../../../core/services/inara-sync-bridge.service';
import { SyncHelpModalService } from '../../../core/services/sync-help-modal.service';
import { SyncLogService } from '../../../core/services/sync-log.service';
import type { GuildSystemBgsDto, SystemsFilterValue } from '../../../core/models/guild-systems.model';
import { getInfluenceClass } from '../../../core/utils/influence-thresholds.util';
import { hasConflictState } from '../../../core/utils/guild-systems.util';
import { getInaraFreshnessBadge } from '../../../core/utils/inara-freshness.util';
import {
  getOriginalInaraStatusSummary,
  isInaraWithoutNewsCategory,
  shouldShowRumorsInsteadOfStatus,
} from '../../../core/utils/inara-data-derivation.util';

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
  filterOpen = signal(false);

  toggleFilter(): void {
    if (this.filterOpen()) {
      this.filterOpen.set(false);
      this.systemFilterQuery.set('');
    } else {
      this.filterOpen.set(true);
    }
  }
  /** ID du système dont le nom vient d'être copié (feedback tooltip "Copié") */
  copiedSystemId = signal<number | null>(null);
  /** Menu contextuel (clic droit) sur une ligne système */
  contextMenuOpen = signal(false);
  contextMenuSys = signal<GuildSystemBgsDto | null>(null);
  contextMenuPos = signal({ x: 0, y: 0 });
  /** Filtre textuel sur les noms de systèmes */
  systemFilterQuery = signal('');

  /** Sections collapseables : low, healthy, others. true = section visible (expanded). Collapsed par défaut. */
  lowExpanded = signal(false);
  healthyExpanded = signal(false);
  othersExpanded = signal(false);
  withoutNewsExpanded = signal(false);

  /**
   * Toutes les sections repliables **affichées** (selon filtre carte / recherche) sont dépliées.
   * Si aucune section repliable n’est dans la liste courante → false (pipeline prend de la place).
   */
  readonly allCollapsibleSectionsExpanded = computed(() => {
    const cats = this.categoryConfig();
    const collapsible = cats.filter((c) => this.isCollapsible(c.key));
    if (collapsible.length === 0) return false;
    return collapsible.every((c) => this.isSectionExpanded(c.key));
  });
  /**
   * Toutes les sections repliables présentes dans la liste courante sont repliées.
   * Utilisé pour réduire la hauteur du pavé au contenu (sans flex pleine colonne).
   */
  readonly allCollapsibleSectionsCollapsed = computed(() => {
    const cats = this.categoryConfig();
    const collapsible = cats.filter((c) => this.isCollapsible(c.key));
    if (collapsible.length === 0) return false;
    return collapsible.every((c) => !this.isSectionExpanded(c.key));
  });
  /** Notifie le dashboard pour adapter la grille (pipeline vs panneau systèmes). */
  readonly allSectionsExpandedChange = output<boolean>();
  /** Notifie le dashboard : replier la zone systèmes à la hauteur du contenu. */
  readonly allCollapsibleSectionsCollapsedChange = output<boolean>();

  panelState = this.guildSync.panelState;
  systems = this.guildSync.systems;
  lastError = this.guildSync.lastError;

  emptyMessage = computed(() => {
    const s = this.panelState();
    if (s === 'loading') return 'Chargement...';
    return 'Aucun système';
  });

  protected readonly totalSystemCount = computed(() => {
    const s = this.systems();
    const ids = new Set<number>();
    for (const sys of [
      ...(s.origin ?? []),
      ...(s.headquarter ?? []),
      ...(s.surveillance ?? []),
      ...(s.critical ?? []),
      ...(s.low ?? []),
      ...(s.healthy ?? []),
      ...(s.others ?? []),
    ]) { ids.add(sys.id); }
    return ids.size;
  });

  ngOnInit(): void {
    this.guildSync.loadSystems();
  }

  constructor() {
    effect(() => {
      const name = this.guildSync.mapSelectedSystemName();
      if (name === null) this.systemFilterQuery.set('');
    });
    effect(() => {
      const id = this.guildSync.highlightedSystemIdInList();
      if (!id) return;
      this.guildSync.systemsFilter.set('all');
      const s = this.systems();
      const allForWn = [
        ...(s.origin ?? []),
        ...(s.headquarter ?? []),
        ...(s.surveillance ?? []),
        ...(s.conflicts ?? []),
        ...(s.critical ?? []),
        ...(s.low ?? []),
        ...(s.healthy ?? []),
        ...(s.others ?? []),
      ];
      const hit = allForWn.find((sys) => sys.id === id);
      if (hit && isInaraWithoutNewsCategory(hit)) this.withoutNewsExpanded.set(true);
      for (const [catKey, arr] of [
        ['low', s.low],
        ['healthy', s.healthy],
        ['others', s.others],
      ] as const) {
        if (arr?.some((sys) => sys.id === id)) {
          if (catKey === 'low') this.lowExpanded.set(true);
          if (catKey === 'healthy') this.healthyExpanded.set(true);
          if (catKey === 'others') this.othersExpanded.set(true);
          break;
        }
      }
      setTimeout(() => {
        const el = document.querySelector(`[data-system-id="${id}"]`);
        (el as HTMLElement)?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      }, 100);
    });
    effect(() => {
      this.allSectionsExpandedChange.emit(this.allCollapsibleSectionsExpanded());
    });
    effect(() => {
      this.allCollapsibleSectionsCollapsedChange.emit(this.allCollapsibleSectionsCollapsed());
    });
  }

  onFocusMapOnSystem(event: Event, systemId: number): void {
    event.stopPropagation();
    this.guildSync.requestFocusOnSystem(systemId);
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

  onSystemContextMenu(event: MouseEvent, sys: GuildSystemBgsDto): void {
    event.preventDefault();
    event.stopPropagation();
    this.contextMenuSys.set(sys);
    this.contextMenuPos.set({ x: event.clientX, y: event.clientY });
    this.contextMenuOpen.set(true);
  }

  onContextMenuHq(sys: GuildSystemBgsDto): void {
    this.contextMenuOpen.set(false);
    if (this.toggling() || this.panelState() === 'loading') return;
    this.toggling.set(true);
    this.api.toggleHeadquarter(sys.id).subscribe({
      next: () => {
        this.syncLog.addLog(`HQ: ${sys.name} mis à jour`);
        this.guildSync.loadSystems();
      },
      error: (err) => {
        const msg = err?.error?.error ?? err?.error?.message ?? err?.message ?? 'Erreur';
        this.syncLog.addLog('HQ échec: ' + msg);
        this.toggling.set(false);
      },
      complete: () => this.toggling.set(false),
    });
  }

  onContextMenuSurveillance(sys: GuildSystemBgsDto): void {
    this.contextMenuOpen.set(false);
    if (this.toggling() || this.panelState() === 'loading') return;
    this.toggling.set(true);
    this.api.toggleSurveillance(sys.id).subscribe({
      next: () => {
        this.syncLog.addLog(`Surveillance: ${sys.name} mis à jour`);
        this.guildSync.loadSystems();
      },
      error: (err) => {
        const msg = err?.error?.error ?? err?.error?.message ?? err?.message ?? 'Erreur';
        this.syncLog.addLog('Surveillance échec: ' + msg);
        this.toggling.set(false);
      },
      complete: () => this.toggling.set(false),
    });
  }

  closeContextMenu(): void {
    this.contextMenuOpen.set(false);
  }

  /** Classe couleur d'influence — seuils métier (5%, 15%, 60%). */
  getInfluenceClass(sys: GuildSystemBgsDto): string {
    return getInfluenceClass(sys.influencePercent);
  }

  /** Vrai si delta affichable (existe et arrondi 2 déc ≠ 0). Pas de placeholder si absent. */
  hasDelta(delta: number | null | undefined): boolean {
    if (delta == null) return false;
    const rounded = Math.round(delta * 100) / 100;
    return rounded !== 0;
  }

  /** Format delta pour UI : ↑ + 0,03% (vert) / ↓ - 0,12% (rouge). Vide si 0.00 après arrondi. */
  getDeltaDisplay(delta: number | null | undefined): string {
    if (delta == null) return '';
    const rounded = Math.round(delta * 100) / 100;
    if (rounded === 0) return '';
    const sign = rounded >= 0 ? '+' : '-';
    const val = Math.abs(rounded).toFixed(2).replace('.', ',');
    const arrow = rounded >= 0 ? '↑' : '↓';
    return `${arrow} ${sign} ${val}%`;
  }

  /** Classe CSS pour couleur du delta : delta--up (vert) ou delta--down (rouge). */
  getDeltaClass(delta: number | null | undefined): 'delta--up' | 'delta--down' | null {
    if (!this.hasDelta(delta) || delta == null) return null;
    return delta >= 0 ? 'delta--up' : 'delta--down';
  }

  /** Badge circulaire : âge des données Inara (voir inara-freshness.util). */
  protected readonly inaraFreshness = getInaraFreshnessBadge;

  /** Badges d'état affichables sur une ligne système. */
  getStateBadges(sys: GuildSystemBgsDto): string[] {
    if (shouldShowRumorsInsteadOfStatus(sys)) return ['Rumeurs'];
    const badges: string[] = [];
    const fromStates = sys.states?.length ? sys.states : (sys.state?.trim() ? [sys.state.trim()] : []);
    for (const s of fromStates) {
      if (s && !badges.includes(s)) badges.push(s);
    }
    if (sys.isExpansionCandidate && !badges.includes('Expansion')) badges.push('Expansion');
    if (sys.isThreatened && !badges.includes('Menacé')) badges.push('Menacé');
    return badges;
  }

  /** Tooltip : statut Inara d’origine lorsque le libellé affiché est « Rumeurs ». */
  getStateBadgeTitle(sys: GuildSystemBgsDto, badge: string): string {
    if (badge !== 'Rumeurs') return '';
    const orig = getOriginalInaraStatusSummary(sys);
    return orig
      ? `Indication Inara (données 7–30 j) : ${orig}`
      : 'Données Inara entre 7 et 30 jours — statut à prendre avec précaution';
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

  /** Ouvre le système sur Inara dans un nouvel onglet. Si inaraUrl est présent, ouvre la page directe, sinon recherche par nom. */
  openSystemInara(sys: GuildSystemBgsDto): void {
    const url = sys.inaraUrl && sys.inaraUrl.trim()
      ? sys.inaraUrl.trim()
      : `https://inara.cz/elite/search/?search=${encodeURIComponent(sys.name)}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  }

  protected toggleLow = (): void => this.lowExpanded.update((v) => !v);
  protected toggleHealthy = (): void => this.healthyExpanded.update((v) => !v);
  protected toggleOthers = (): void => this.othersExpanded.update((v) => !v);
  protected toggleWithoutNews = (): void => this.withoutNewsExpanded.update((v) => !v);

  protected toggleSection(catKey: string): void {
    if (catKey === 'low') this.toggleLow();
    else if (catKey === 'healthy') this.toggleHealthy();
    else if (catKey === 'others') this.toggleOthers();
    else if (catKey === 'withoutNews') this.toggleWithoutNews();
  }

  protected isCollapsible(catKey: string): boolean {
    return catKey === 'low' || catKey === 'healthy' || catKey === 'others' || catKey === 'withoutNews';
  }

  protected isSectionExpanded(catKey: string): boolean {
    if (catKey === 'low') return this.lowExpanded();
    if (catKey === 'healthy') return this.healthyExpanded();
    if (catKey === 'others') return this.othersExpanded();
    if (catKey === 'withoutNews') return this.withoutNewsExpanded();
    return true;
  }

  private filterSystemsByQuery(systems: GuildSystemBgsDto[], query: string): GuildSystemBgsDto[] {
    if (!query || !query.trim()) return systems;
    const q = query.trim().toLowerCase();
    return systems.filter((sys) => sys.name.toLowerCase().includes(q));
  }

  /** Configuration des catégories pour le template unique. Filtre appliqué via systemsFilter + systemFilterQuery. */
  protected categoryConfig = computed(() => {
    const s = this.systems();
    const filter = this.guildSync.systemsFilter();
    const query = this.systemFilterQuery();
    const allList = [
      ...(s.origin ?? []),
      ...(s.headquarter ?? []),
      ...(s.surveillance ?? []),
      ...(s.conflicts ?? []),
      ...(s.critical ?? []),
      ...(s.low ?? []),
      ...(s.healthy ?? []),
      ...(s.others ?? []),
    ];
    const conflictSeen = new Set<number>();
    const systemsWithConflict = allList.filter((sys) => {
      if (!hasConflictState(sys)) return false;
      if (conflictSeen.has(sys.id)) return false;
      conflictSeen.add(sys.id);
      return true;
    });
    const seenWithoutNews = new Set<number>();
    const systemsWithoutNews: GuildSystemBgsDto[] = [];
    for (const sys of allList) {
      if (seenWithoutNews.has(sys.id)) continue;
      if (!isInaraWithoutNewsCategory(sys)) continue;
      seenWithoutNews.add(sys.id);
      systemsWithoutNews.push(sys);
    }
    let raw: { key: SystemsFilterValue; label: string; systems: GuildSystemBgsDto[] }[] = [
      { key: 'origin', label: 'Origine', systems: s.origin ?? [] },
      { key: 'hq', label: 'Quartier général', systems: s.headquarter ?? [] },
      { key: 'surveillance', label: 'Systèmes sous surveillance', systems: s.surveillance ?? [] },
      { key: 'conflicts', label: 'Systèmes en conflits', systems: systemsWithConflict },
      { key: 'critical', label: 'Systèmes critiques', systems: s.critical ?? [] },
      { key: 'healthy', label: 'Systèmes sains', systems: s.healthy ?? [] },
      { key: 'low', label: 'Systèmes bas', systems: s.low ?? [] },
      { key: 'others', label: 'Autres', systems: s.others ?? [] },
      { key: 'withoutNews', label: 'Sans signal', systems: systemsWithoutNews },
    ];
    raw = raw.map((cat) => ({
      ...cat,
      systems: this.filterSystemsByQuery(cat.systems, query),
    }));
    if (filter === 'all') return raw;
    if (filter === 'conflicts') return raw.filter((c) => c.key === 'conflicts');
    if (filter === 'surveillance') return raw.filter((c) => c.key === 'surveillance');
    if (filter === 'withoutNews') return raw.filter((c) => c.key === 'withoutNews');
    const match = raw.find((c) => c.key === filter);
    return match ? [match] : raw;
  });
}
