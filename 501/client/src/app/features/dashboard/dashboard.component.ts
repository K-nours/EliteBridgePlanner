import { Component, OnInit, OnDestroy, inject, signal, computed, effect, ViewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { catchError, forkJoin, map, of, switchMap, take } from 'rxjs';
import { TruncateTooltipDirective } from '../../shared/directives/truncate-tooltip.directive';
import { SettingsModalComponent } from '../../shared/components/settings-modal/settings-modal.component';
import { SyncHelpModalComponent } from '../../shared/components/sync-help-modal/sync-help-modal.component';
import { GuildSystemsPanelComponent } from './guild-systems-panel/guild-systems-panel.component';
import { GuildSystemsMapComponent } from './guild-systems-map/guild-systems-map.component';
import { MapPanelComponent } from './map-panel/map-panel.component';
import { SyncStatusPanelComponent } from './sync-status-panel/sync-status-panel.component';
import { ChantiersDebugPanelComponent } from './chantiers-debug-panel/chantiers-debug-panel.component';
import { ChantierLogisticsPanelComponent } from './chantier-logistics-panel/chantier-logistics-panel.component';
import { DiplomaticPipelinePanelComponent } from './diplomatic-pipeline-panel/diplomatic-pipeline-panel.component';
import { FrontierCmdrPanelComponent } from './frontier-cmdr-panel/frontier-cmdr-panel.component';
import { CmdrsPanelComponent } from './cmdrs-panel/cmdrs-panel.component';
import { ReunionPanelComponent } from './reunion-panel/reunion-panel.component';
import { ReveilPanelComponent } from './reveil-panel/reveil-panel.component';
import { DashboardApiService } from '../../core/services/dashboard-api.service';
import { CommandersApiService } from '../../core/services/commanders-api.service';
import { SyncLogService } from '../../core/services/sync-log.service';
import { GuildSystemsSyncService } from '../../core/services/guild-systems-sync.service';
import { GuildSystemsApiService } from '../../core/services/guild-systems-api.service';
import { FrontierAuthService } from '../../core/services/frontier-auth.service';
import { GuildSettingsService } from '../../core/services/guild-settings.service';
import { InaraSyncBridgeService } from '../../core/services/inara-sync-bridge.service';
import { SyncHelpModalService } from '../../core/services/sync-help-modal.service';
import { FrontierJournalApiService } from '../../core/services/frontier-journal-api.service';
import { RareCommodityAlertService } from '../../core/services/rare-commodity-alert.service';
import type { DashboardResponseDto } from '../../core/models/dashboard.model';
import type { CommandersResponseDto } from '../../core/models/commanders.model';
import type { SystemsFilterValue, GuildSystemBgsDto } from '../../core/models/guild-systems.model';
import type {
  FrontierJournalUnifiedSyncStatusDto,
  FrontierJournalParseStatusDto,
  FrontierJournalSystemDerivedDto,
} from '../../core/services/frontier-journal-api.service';
import { hasConflictState } from '../../core/utils/guild-systems.util';
import { isInaraWithoutNewsCategory } from '../../core/utils/inara-data-derivation.util';
import { inaraAgeDays } from '../../core/utils/inara-freshness.util';
import { AVATAR_DEFAULT_FALLBACK_URL } from '../../core/constants/avatar.constants';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [TruncateTooltipDirective, SettingsModalComponent, SyncHelpModalComponent, GuildSystemsPanelComponent, GuildSystemsMapComponent, MapPanelComponent, SyncStatusPanelComponent, ChantiersDebugPanelComponent, ChantierLogisticsPanelComponent, DiplomaticPipelinePanelComponent, FrontierCmdrPanelComponent, CmdrsPanelComponent, ReunionPanelComponent, ReveilPanelComponent],
  template: `
    <div class="page">
      <div class="page-bg">
        <img class="page-bg-img" src="https://inara.cz/data/gallery/180/180177x4304.jpg" alt="" draggable="false" />
        <div class="page-bg-overlay"></div>
      </div>
      <header class="header-zone">
        <h1 class="header-faction">{{ factionName() }}</h1>
        <div class="header-emblem-wrapper">
          <span class="header-emblem-flank header-emblem-flank--left">GALACTIC</span>
          <div class="emblem-spacer">
            <div class="emblem-box-wrapper">
              @if (refreshProgress() > 0) {
              <svg class="emblem-progress" viewBox="0 0 72 72">
                <circle class="emblem-progress-bg" cx="36" cy="36" r="34" fill="none" stroke-width="2" />
                <circle class="emblem-progress-fill" cx="36" cy="36" r="34" fill="none" stroke-width="2"
                  [attr.stroke-dasharray]="strokeCircumference"
                  [attr.stroke-dashoffset]="strokeDashOffset()" />
              </svg>
            }
              <button type="button" class="emblem-box" (click)="onSyncSystemsClick()" [disabled]="refreshProgress() > 0"
                truncateTooltip="Synchroniser les systèmes" [truncateTooltipForce]="true">
                <img class="emblem-img" src="assets/squadron-emblem.png" alt="Squadron emblem" />
              </button>
            </div>
          </div>
          <span class="header-emblem-flank header-emblem-flank--right">CONTROL</span>
        </div>
        <div class="header-frontier">
          @if (frontierAuth.loading()) {
            <span class="frontier-status frontier-loading">...</span>
          } @else if (frontierAuth.isConnected()) {
            <div class="frontier-dropdown">
              @if (frontierMenuOpen()) {
                <div class="frontier-menu-backdrop" (click)="frontierMenuOpen.set(false)"></div>
              }
              <div class="frontier-cmdr-trigger" role="button" tabindex="0"
                (click)="frontierMenuOpen.set(!frontierMenuOpen())"
                (keydown.enter)="frontierMenuOpen.set(!frontierMenuOpen())"
                (keydown.space.prevent)="frontierMenuOpen.set(!frontierMenuOpen())">
                <span class="frontier-cmdr-name">{{ frontierAuth.commanderName() ?? 'CMDR' }}</span>
                <div class="frontier-cmdr-avatar">
                  @if (connectedCmdrAvatar() && !headerAvatarError()) {
                    <img [src]="connectedCmdrAvatar()!" [alt]="frontierAuth.commanderName() ?? 'CMDR'" referrerpolicy="no-referrer"
                      (error)="headerAvatarError.set(true)" />
                  }
                  @if (!connectedCmdrAvatar() || headerAvatarError()) {
                    <span class="frontier-cmdr-initial">{{ (frontierAuth.commanderName() ?? 'C').charAt(0).toUpperCase() }}</span>
                  }
                </div>
              </div>
              @if (frontierMenuOpen()) {
                <div class="frontier-menu">
                  <button type="button" class="frontier-menu-item frontier-menu-item--neutral" (click)="openSettings(); frontierMenuOpen.set(false)">
                    Paramètres
                  </button>
                  <button type="button"
                    class="frontier-menu-item frontier-menu-item--neutral"
                    [disabled]="!guildSettings.inaraCmdrUrl()"
                    (click)="onSyncCmdrAvatarClick(); frontierMenuOpen.set(false)">
                    Sync avatar CMDR
                  </button>
                  <button type="button" class="frontier-menu-item frontier-menu-item--neutral" (click)="onUpdateScriptClick(); frontierMenuOpen.set(false)">
                    Mettre à jour le script Inara Sync
                  </button>
                  <button type="button" class="frontier-menu-item" (click)="frontierAuth.logout(); frontierMenuOpen.set(false)">
                    Déconnexion
                  </button>
                </div>
              }
            </div>
          } @else if (frontierAuth.needsReconnect()) {
            @if (frontierAuth.commanderName(); as name) {
              <span class="frontier-cmdr frontier-expired">{{ name }}</span>
            }
            @if (frontierAuth.errorMessage(); as errMsg) {
              <span class="frontier-error-msg" [title]="errMsg">{{ errMsg }}</span>
            }
            <button type="button" class="btn-frontier-reconnect" (click)="frontierAuth.login()">Reconnecter</button>
          } @else {
            <button type="button" class="btn-frontier-login" (click)="frontierAuth.login()">Connexion Frontier</button>
          }
        </div>
      </header>
      <main class="main-grid">
        <div class="col col-left">
          <div class="col-left-systems-fill box"
            [class.col-left--systems-all-expanded]="systemsPanelAllExpanded()"
            [class.col-left-systems-fill--shrink]="systemsPanelAllSectionsCollapsed()">
            <app-guild-systems-panel
              (allSectionsExpandedChange)="systemsPanelAllExpanded.set($event)"
              (allCollapsibleSectionsCollapsedChange)="systemsPanelAllSectionsCollapsed.set($event)" />
          </div>
          <app-diplomatic-pipeline-panel class="box" />
        </div>
        <div class="col col-center">
          <div class="map-section">
            <app-map-panel class="box map-box"
              [mapViewMode]="mapViewMode()"
              [journalCmdrMapPoints]="journalCmdrMapPoints()"
              [systems]="guildSystemsSync.systems()"
              [systemsFilter]="guildSystemsSync.systemsFilter()"
              [journalLayerVisited]="journalMapLayerVisited()"
              [journalLayerDiscovered]="journalMapLayerDiscovered()"
              [journalLayerFullScan]="journalMapLayerFullScan()"
              [journalDerivedByName]="journalDerivedByName()"
              [mapFilterCountsLeft]="mapFilterCountsLeft()"
              [mapFilterCountsRight]="mapFilterCountsRight()"
              [journalCmdrCountVisited]="journalCmdrCountVisited()"
              [journalCmdrCountDiscovered]="journalCmdrCountDiscovered()"
              [journalCmdrCountFullScan]="journalCmdrCountFullScan()"
              (mapViewModeChange)="mapViewMode.set($event)"
              (systemsFilterChange)="setSystemsFilter($event)"
              (journalLayerChange)="selectJournalMapLayer($event)" />
          </div>
          <app-sync-status-panel class="box box-sync-status"
            [class.box-sync-status--collapsed]="syncStatusCollapsed()"
            [syncStatusCollapsed]="syncStatusCollapsed()"
            [syncLogsWithRecap]="syncLogsWithRecap()"
            [syncLogLines]="syncLogLines()"
            [mapViewMode]="mapViewMode()"
            [hasSyncLogs]="syncLog.logs().length > 0"
            (toggleCollapsed)="toggleSyncStatusCollapsed()"
            (copyLogs)="copyLogsToClipboard()"
            (clearLogs)="clearLogs()" />
          <div class="bottom-row-rest">
            <app-chantiers-debug-panel class="box box-pipeline-dipo" [class.box-pipeline-dipo--compact]="systemsPanelAllExpanded()" />
            <app-chantier-logistics-panel class="box box-chantier-logistics" />
          </div>
        </div>
        <div class="col col-right">
          @if (showCmdrConnected() && dashboard()?.frontierProfile; as fp) {
            <app-frontier-cmdr-panel class="box"
              [frontierProfile]="fp"
              [connectedCmdrAvatar]="connectedCmdrAvatar()"
              [journalUnifiedRunning]="journalUnifiedRunning()"
              [journalUnifiedStatus]="journalUnifiedStatus()"
              [journalFrontierTooltip]="journalFrontierTooltip()"
              [rareCommodityHasAlert]="rareCommodityAlert.hasAlert()"
              [rareCommodityAlertUrl]="rareCommodityAlert.alertUrl()"
              [rareCommodityAlertTooltip]="rareCommodityAlert.alertTooltip()"
              [rareCommodityIsReady]="rareCommodityAlert.isReady()"
              [rareCommodityOkTooltip]="rareCommodityAlert.okTooltip()"
              (journalExport)="onJournalExportClick()"
              (journalImportReplace)="triggerJournalImportReplace()"
              (syncJournal)="onSyncJournalClick()"
              (connectFrontierForJournal)="onConnectFrontierForJournal()"
              (journalFileSelected)="onJournalImportFileSelected($event)" />
          }
          <app-cmdrs-panel class="box"
            [commandersData]="commandersForList()"
            [currentCmdrName]="frontierAuth.commanderName()"
            [inaraSquadronUrl]="guildSettings.inaraSquadronUrl()"
            [syncAvatarsRosterTooltip]="syncAvatarsRosterTooltip()"
            [syncCmdrsTooltip]="syncCmdrsTooltip()"
            [avatarDefaultFallbackUrl]="AVATAR_DEFAULT_FALLBACK_URL"
            (syncAvatars)="onSyncAvatarsRosterClick()"
            (syncCmdrs)="onSyncCmdrsClick()" />
          <app-reunion-panel class="box box-reunion" />
          <app-reveil-panel class="box"
            [reveilSystems]="reveilSystems()"
            [copiedReveilSystemId]="copiedReveilSystemId()"
            (openInara)="openReveilSystemInara($event.inaraUrl, $event.name)"
            (copySystemName)="copyReveilSystemName($event.event, $event.name, $event.id)" />
        </div>
      </main>
      <app-settings-modal #settingsModal />
      <app-sync-help-modal
        [visible]="syncHelpModal.visible()"
        (closed)="syncHelpModal.hide()" />
    </div>
  `,
  styles: [`
    :host {
      display: block;
      width: 100%;
    }
    .page {
      position: relative;
      display: flex;
      flex-direction: column;
      min-height: 100vh;
      width: 100%;
      max-width: 100vw;
      overflow-x: clip;
    }
    .page-bg {
      position: fixed;
      inset: 0;
      z-index: 0;
      background-color: #060a0f;
      pointer-events: none;
    }
    .page-bg-img {
      position: absolute;
      inset: 0;
      width: 100%;
      height: 100%;
      object-fit: cover;
      pointer-events: none;
      user-select: none;
      -webkit-user-drag: none;
    }
    .page-bg-overlay {
      position: absolute;
      inset: 0;
      background-image:
        radial-gradient(ellipse at 30% 20%, rgba(0, 80, 120, 0.2) 0%, transparent 50%),
        radial-gradient(ellipse at 70% 80%, rgba(0, 212, 255, 0.1) 0%, transparent 40%),
        linear-gradient(180deg, rgba(6, 10, 15, 0.5) 0%, rgba(10, 18, 32, 0.35) 50%, rgba(6, 10, 15, 0.5) 100%),
        linear-gradient(rgba(6, 10, 15, 0.25), rgba(6, 10, 15, 0.25));
      pointer-events: none;
    }
    .header-zone {
      position: relative;
      z-index: 2;
      min-height: 56px;
      overflow: visible;
      width: 100%;
      background: rgba(6, 20, 35, 0.88);
      border-bottom: 1px solid rgba(0, 212, 255, 0.22);
      padding: 1rem;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
    }
    .header-faction {
      margin: 0;
      font-size: 1.1rem;
      font-family: 'Orbitron', sans-serif;
      font-weight: 600;
      color: #00eaff;
      text-shadow:
        0 0 4px rgba(0, 234, 255, 0.4),
        0 0 8px rgba(0, 234, 255, 0.25);
      flex-shrink: 0;
    }
    .header-frontier {
      flex-shrink: 0;
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 0.75rem;
      font-family: 'Exo 2', sans-serif;
    }
    .frontier-dropdown {
      position: relative;
      z-index: 102;
    }
    .frontier-menu-backdrop {
      position: fixed;
      inset: 0;
      z-index: 100;
      cursor: default;
    }
    .frontier-cmdr-trigger {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      background: none;
      border: none;
      padding: 0;
      cursor: pointer;
      font: inherit;
    }
    .frontier-cmdr-trigger .frontier-cmdr-name {
      color: #00d4ff;
      font-weight: 500;
    }
    .frontier-cmdr-trigger:hover .frontier-cmdr-name {
      color: #00eaff;
      text-decoration: underline;
    }
    .frontier-cmdr-avatar {
      width: 36px;
      height: 36px;
      border-radius: 8px;
      background: rgba(0, 212, 255, 0.2);
      border: 1px solid rgba(0, 212, 255, 0.4);
      display: flex;
      align-items: center;
      justify-content: center;
      overflow: hidden;
      flex-shrink: 0;
    }
    .frontier-cmdr-avatar img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .frontier-cmdr-initial {
      font-family: 'Orbitron', sans-serif;
      font-size: 0.75rem;
      font-weight: 700;
      color: #00d4ff;
    }
    .frontier-cmdr {
      color: rgba(110, 231, 183, 0.95);
      font-weight: 500;
    }
    .frontier-menu {
      position: absolute;
      top: 100%;
      right: 0;
      margin-top: 0.25rem;
      min-width: 140px;
      background: rgba(6, 20, 35, 0.98);
      border: 1px solid rgba(0, 212, 255, 0.25);
      border-radius: 8px;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
      z-index: 101;
      overflow: hidden;
    }
    .frontier-menu-item {
      display: block;
      width: 100%;
      padding: 0.5rem 0.75rem;
      font-size: 0.7rem;
      font-family: 'Exo 2', sans-serif;
      background: none;
      border: none;
      color: rgba(255, 255, 255, 0.9);
      cursor: pointer;
      text-align: left;
      transition: background 0.15s;
    }
    .frontier-menu-item--neutral:hover {
      background: rgba(0, 212, 255, 0.15);
      color: #00d4ff;
    }
    .frontier-menu-item--neutral:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
    .frontier-menu-item:not(.frontier-menu-item--neutral):hover {
      background: rgba(255, 100, 100, 0.2);
      color: #ff6b6b;
    }
    .frontier-cmdr.frontier-expired {
      color: rgba(255, 180, 100, 0.9);
    }
    .frontier-error-msg {
      font-size: 0.6rem;
      color: rgba(255, 150, 100, 0.9);
      max-width: 160px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .frontier-loading {
      color: rgba(255, 255, 255, 0.5);
    }
    .btn-frontier-login,
    .btn-frontier-reconnect {
      padding: 0.35rem 0.65rem;
      font-size: 0.65rem;
      font-family: 'Orbitron', sans-serif;
      border-radius: 6px;
      cursor: pointer;
      border: 1px solid rgba(0, 212, 255, 0.4);
      background: rgba(0, 212, 255, 0.12);
      color: #00d4ff;
      transition: background 0.2s, border-color 0.2s;
    }
    .btn-frontier-login:hover,
    .btn-frontier-reconnect:hover {
      background: rgba(0, 212, 255, 0.2);
      border-color: rgba(0, 212, 255, 0.6);
    }
    .btn-frontier-reconnect {
      border-color: rgba(255, 180, 100, 0.5);
      background: rgba(255, 180, 100, 0.12);
      color: #ffb464;
    }
    .btn-frontier-reconnect:hover {
      background: rgba(255, 180, 100, 0.2);
      border-color: rgba(255, 180, 100, 0.7);
    }
    .header-emblem-wrapper {
      position: absolute;
      left: 50%;
      bottom: 0;
      transform: translate(-50%, 50%);
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.5rem;
      flex-shrink: 0;
      z-index: 3;
      pointer-events: auto;
    }
    .header-emblem-flank {
      font-family: 'Orbitron', sans-serif;
      font-size: 0.5rem;
      font-weight: 600;
      color: rgba(255, 255, 255, 0.85);
      text-transform: uppercase;
      letter-spacing: 0.12em;
      line-height: 1;
      display: inline-flex;
      align-items: center;
      transform: translateY(4px);
      margin-bottom: 32px;
    }
    .emblem-spacer {
      position: relative;
      width: 72px;
      height: 72px;
      flex-shrink: 0;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .emblem-box-wrapper {
      position: relative;
      z-index: 3;
    }
    .emblem-progress {
      position: absolute;
      inset: -2px;
      width: 76px;
      height: 76px;
      pointer-events: none;
      transform: rotate(-90deg);
    }
    .emblem-progress-bg {
      stroke: rgba(0, 212, 255, 0.2);
    }
    .emblem-progress-fill {
      stroke: rgba(0, 234, 255, 0.9);
      stroke-linecap: round;
      transition: stroke-dashoffset 0.1s linear;
    }
    .emblem-box {
      width: 72px;
      height: 72px;
      border-radius: 16px;
      border: 1px solid rgba(0, 212, 255, 0.5);
      box-shadow: 0 0 6px rgba(0, 212, 255, 0.25);
      background: rgba(6, 20, 35, 0.95);
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 4px;
      overflow: hidden;
      cursor: pointer;
      appearance: none;
      font: inherit;
      transition: transform 0.2s ease, box-shadow 0.2s ease, border-color 0.2s ease;
    }
    .emblem-box:disabled {
      cursor: wait;
      opacity: 0.9;
    }
    .emblem-box:hover:not(:disabled) {
      transform: scale(1.06);
      border-color: rgba(0, 234, 255, 0.9);
      box-shadow: 0 0 10px rgba(0, 234, 255, 0.4), 0 0 18px rgba(0, 234, 255, 0.2);
    }
    .emblem-box:focus-visible {
      outline: 2px solid rgba(0, 234, 255, 0.8);
      outline-offset: 2px;
    }
    .emblem-img {
      width: 100%;
      height: 100%;
      object-fit: contain;
    }
    .main-grid {
      position: relative;
      z-index: 1;
      flex: 1;
      min-height: 0;
      display: grid;
      /* minmax(0, 1fr) pour que le centre prenne l'espace restant */
      grid-template-columns: minmax(160px, 1fr) minmax(0, 2fr) minmax(160px, 1fr);
      grid-template-rows: 1fr;
      align-items: stretch;
      /* Variable CSS partagée par .col et .bottom-row — garantit un espacement identique partout */
      --layout-gap: 1rem;
      gap: var(--layout-gap);
      width: 100%;
      max-width: 100%;
      margin: 0;
      padding: calc(36px + 0.75rem) 1rem 1rem;
      box-sizing: border-box;
    }
    @media (min-width: 1200px) {
      .header-zone { padding: 1.5rem; }
      .main-grid {
        width: 100%;
        grid-template-columns: minmax(200px, 400px) minmax(0, 1fr) minmax(200px, 400px);
        --layout-gap: 1.5rem;
        gap: var(--layout-gap);
        padding: calc(36px + 0.75rem) 1.5rem 1.5rem;
        box-sizing: border-box;
      }
    }
    @media (min-width: 900px) and (max-width: 1199px) {
      .header-zone { padding: 1.5rem; }
      .main-grid {
        --layout-gap: 1.5rem;
        gap: var(--layout-gap);
        padding: calc(36px + 0.75rem) 1.5rem 1.5rem;
      }
    }
    @media (max-width: 768px) {
      .main-grid {
        grid-template-columns: 1fr;
      }
      .box-row,
      .bottom-row {
        grid-template-columns: 1fr;
      }
      .bottom-row-rest {
        grid-column: 1;
        grid-template-columns: 1fr;
      }
      app-reveil-panel {
        grid-column: 1;
      }
    }
    .col {
      display: flex;
      flex-direction: column;
      gap: var(--layout-gap, 1rem);
      min-width: 0;
    }
    .col-left {
      min-height: 0;
      height: 100%;
      align-self: stretch;
    }
    .col-center {
      width: 100%;
      min-height: 0;
    }
    .col-center .map-section,
    .col-center .box-sync-status,
    .col-center .box {
      width: 100%;
    }
.col-right .box {
      flex: 1 1 auto;
      min-height: 0;
    }
    .col-left app-diplomatic-pipeline-panel.box {
      flex: 0 0 20vh;
      min-height: 0;
      overflow: hidden;
    }
    .col-right app-frontier-cmdr-panel.box {
      flex: 0 0 auto;
    }
    .col-right app-reunion-panel.box {
      flex: 0 0 auto;
    }
    .col-right app-reveil-panel.box {
      flex: 1 1 0;
      min-height: 0;
      overflow: hidden;
    }
    /* Panneau systèmes : ~85 % de la hauteur de la colonne (flex 17 vs chantiers 3) */
    .col-left-systems-fill {
      flex: 17 1 0;
      min-height: 0;
      display: flex;
      flex-direction: column;
      overflow: hidden;
      padding: 0;
      transition:
        flex-grow 0.45s cubic-bezier(0.4, 0, 0.2, 1),
        flex-shrink 0.45s cubic-bezier(0.4, 0, 0.2, 1),
        flex-basis 0.45s cubic-bezier(0.4, 0, 0.2, 1);
    }
    /* Sections toutes repliées : on garde la hauteur max, le contenu est scrollable. */
    .col-left-systems-fill--shrink {
      flex: 17 1 0;
      overflow-y: auto;
    }
    /* Chantiers en cours : ~15 % de la hauteur de la colonne (flex 3 vs systèmes 17) */
    /* box-pipeline-dipo est maintenant dans .bottom-row — overflow: hidden géré par .bottom-row > .box */
    .box-pipeline-dipo {
      transition: padding 0.4s cubic-bezier(0.4, 0, 0.2, 1);
    }
    .box-pipeline-dipo--compact {
      padding: 0.45rem;
    }
    .box-pipeline-dipo--compact > h3 {
      margin: 0;
    }
    .box {
      background: rgba(6, 20, 35, 0.88);
      border: 1px solid rgba(0, 212, 255, 0.14);
      border-radius: 10px;
      box-shadow:
        0 0 5px rgba(0, 234, 255, 0.03),
        0 0 10px rgba(0, 234, 255, 0.02),
        0 0 20px rgba(0, 234, 255, 0.01);
      transition:
        box-shadow 0.4s cubic-bezier(0.4, 0, 0.2, 1),
        border-color 0.35s ease;
    }
    /* Titres de blocs : même lueur cyan que le panneau chantiers */
    .box h3 {
      margin: 0;
      font-family: 'Orbitron', sans-serif;
      font-size: 0.75rem;
      font-weight: 600;
      color: #00eaff;
      text-transform: uppercase;
      letter-spacing: 0.1em;
      text-shadow:
        0 0 4px rgba(0, 234, 255, 0.4),
        0 0 10px rgba(0, 234, 255, 0.22);
    }
    .box > h3:first-child {
      margin-top: 0;
    }
    .bottom-row-rest {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: var(--layout-gap, 1rem);
      height: 20vh;
      min-height: 0;
      align-items: stretch;
      flex-shrink: 0;
    }
    .bottom-row-rest > .box {
      min-height: 0;
      max-height: 100%;
      overflow: hidden;
      display: flex;
      flex-direction: column;
    }
    .box-chantier-logistics {
      display: flex;
      flex-direction: column;
      min-height: 0;
      flex: 1 1 auto;
      overflow: hidden;
    }
    .box-pipeline-diplomatique {
      display: flex;
      flex-direction: column;
      min-height: 0;
      overflow: hidden;
    }
    .map-section {
      position: relative;
      flex: 1 1 0;
      min-height: 0;
      display: flex;
      flex-direction: column;
    }
    .col-right app-cmdrs-panel.box {
      flex: 0 0 auto;
    }
    @media (prefers-reduced-motion: reduce) {
      .col-left-systems-fill,
      .col-left .box-pipeline-dipo,
      .box {
        transition: none !important;
      }
    }
  `],
})
export class DashboardComponent implements OnInit, OnDestroy {
  @ViewChild('settingsModal') settingsModal!: SettingsModalComponent;

  private readonly dashboardApi = inject(DashboardApiService);
  private readonly commandersApi = inject(CommandersApiService);
  protected readonly guildSettings = inject(GuildSettingsService);
  protected readonly inaraBridge = inject(InaraSyncBridgeService);
  protected readonly syncHelpModal = inject(SyncHelpModalService);
  protected readonly frontierAuth = inject(FrontierAuthService);
  private readonly frontierJournalApi = inject(FrontierJournalApiService);
  protected readonly rareCommodityAlert = inject(RareCommodityAlertService);
  protected readonly frontierMenuOpen = signal(false);
  /** Ligne affichée dans la zone logs sync pendant une synchro journal Frontier. */
  protected readonly journalBackfillProgress = signal<string | null>(null);
  protected readonly journalUnifiedStatus = signal<FrontierJournalUnifiedSyncStatusDto | null>(null);
  protected readonly journalUnifiedRunning = computed(() => this.journalUnifiedStatus()?.isRunning === true);
  /** Calques carte 3D (journal CMDR). */
  protected readonly journalMapLayerVisited = signal(false);
  protected readonly journalMapLayerDiscovered = signal(false);
  protected readonly journalMapLayerFullScan = signal(false);
  /** Clé = nom système normalisé (uppercase), pour la carte. */
  protected readonly journalDerivedByName = signal<
    Record<string, { isVisited: boolean; hasFirstDiscoveryBody: boolean; isFullScanned: boolean }>
  >({});
  /** Liste complète GET derived/systems (coords CMDR pour la vue Cmdr). */
  protected readonly journalDerivedSystemsList = signal<FrontierJournalSystemDerivedDto[]>([]);
  /** Points journal avec coordonnées StarPos pour la carte vue Cmdr. */
  protected readonly journalCmdrMapPoints = computed(() =>
    this.journalDerivedSystemsList().filter(
      (s) => s.coordsX != null && s.coordsY != null && s.coordsZ != null,
    ),
  );
  /** Compteurs calques vue Cmdr (même périmètre que les points sur la carte). */
  protected readonly journalCmdrCountVisited = computed(
    () => this.journalCmdrMapPoints().filter((s) => s.isVisited).length,
  );
  protected readonly journalCmdrCountDiscovered = computed(
    () => this.journalCmdrMapPoints().filter((s) => s.hasFirstDiscoveryBody).length,
  );
  protected readonly journalCmdrCountFullScan = computed(
    () => this.journalCmdrMapPoints().filter((s) => s.isFullScanned).length,
  );
  /** faction = carte guilde + filtres BGS ; cmdr = points journal ; galacticBridge = ponts planifiés. */
  protected readonly mapViewMode = signal<'faction' | 'cmdr' | 'galacticBridge'>('faction');
  protected readonly journalParseStatus = signal<FrontierJournalParseStatusDto | null>(null);
  protected readonly journalFrontierStatusLines = computed((): string[] | null => {
    const u = this.journalUnifiedStatus();
    const ps = this.journalParseStatus();
    if (!u && !ps) return null;
    const lines: string[] = [];
    const lastUtc = u?.lastSyncCompletedUtc;
    if (lastUtc) lines.push(`Dernière synchro journal : ${this.formatJournalIsoUtc(lastUtc)}`);
    if (u?.isRunning && u.lastMessage) {
      lines.push(u.lastMessage);
      return lines;
    }
    if (u?.phase === 'error') {
      if (u.lastMessage) lines.push(u.lastMessage);
      else if (u.lastError) lines.push(`Journal Frontier : erreur — ${u.lastError}`);
      return lines;
    }
    const pending = u?.pendingParseDays ?? ps?.pendingDaysEstimate ?? 0;
    if (pending > 0) {
      lines.push(`Journal Frontier : ~${pending} jour(s) encore à parser`);
    } else if ((ps?.systemsCount ?? 0) > 0 || (u?.fetchedSuccessDaysApprox ?? 0) > 0) {
      const coords = ps?.systemsWithCoordsCount ?? u?.systemsWithCoordsCount ?? 0;
      lines.push(`Journal Frontier : à jour — ${ps?.systemsCount ?? 0} syst., ${coords} sur la carte`);
    } else {
      lines.push('Journal Frontier : prêt — lancez une synchro pour télécharger l’historique');
    }
    return lines.length ? lines : null;
  });
  /** Texte tooltip sur le bouton de synchro journal (état Frontier / dernière synchro). */
  protected readonly journalFrontierTooltip = computed(() => {
    const lines = this.journalFrontierStatusLines();
    if (lines?.length) return lines.join(' — ');
    return 'Journal Frontier';
  });
  /** Toutes les sections Bas / Sains / Autres dépliées → panneau systèmes pleine hauteur, pipeline réduit. */
  protected readonly systemsPanelAllExpanded = signal(false);
  /** Toutes les sections repliables visibles sont repliées → zone systèmes en hauteur contenu (aligné avec l’état initial). */
  protected readonly systemsPanelAllSectionsCollapsed = signal(true);
  /** Importer : 'replace' | merge + duplicatePolicy */
  private readonly journalImportPending = signal<{
    strategy: 'replace' | 'merge';
    duplicatePolicy: 'skip' | 'import';
  } | null>(null);
  protected readonly syncStatusMenuOpen = signal(false);
  /** true = zone des logs masquée (titre + boutons visibles uniquement). */
  protected readonly syncStatusCollapsed = signal(true);
  protected readonly headerAvatarError = signal(false);
  protected readonly syncLog = inject(SyncLogService);
  protected readonly AVATAR_DEFAULT_FALLBACK_URL = AVATAR_DEFAULT_FALLBACK_URL;
  protected readonly guildSystemsSync = inject(GuildSystemsSyncService);
  private readonly guildSystemsApi = inject(GuildSystemsApiService);
  private journalUnifiedPollingRef: ReturnType<typeof setInterval> | null = null;
  private journalUnifiedExpectComplete = false;
  /** Évite les doublons dans les logs pour le même libellé de progression. */
  private journalUnifiedLastLoggedMessage: string | null = null;

  /** Progression EDSM en direct pendant un import (ex: "EDSM : requêtes unitaires (47/173)"). */
  protected systemsImportProgress = signal<string | null>(null);
  private systemsImportPollingRef: ReturnType<typeof setInterval> | null = null;

  protected readonly strokeCircumference = 2 * Math.PI * 34;
  protected refreshProgress = signal(0);

  constructor() {
    effect(() => {
      const url = this.connectedCmdrAvatar();
      if (url) {
        this.headerAvatarError.set(false);
      }
    });
    /** Journal CMDR : rechargé quand Frontier passe à « connecté » (après /api/user/me), pas seulement au 1er tick où l’état était encore déconnecté. */
    effect(() => {
      if (this.frontierAuth.isConnected()) {
        this.refreshJournalParseAndDerived();
        this.loadJournalUnifiedStatus();
      }
    });
  }
  protected strokeDashOffset = computed(() => this.strokeCircumference * (1 - this.refreshProgress() / 100));

  /** Dernières erreurs Inara (postMessage depuis onglet), effacées au succès. */
  protected lastSystemsSyncError = signal<string | null>(null);
  protected lastRosterSyncError = signal<string | null>(null);
  protected lastAvatarSyncError = signal<string | null>(null);

  protected dashboard = signal<DashboardResponseDto | null>(null);
  protected commanders = signal<CommandersResponseDto | null>(null);
  protected commandersForList = computed(() => {
    const data = this.commanders();
    if (!data?.commanders?.length) return data;
    const current = this.frontierAuth.commanderName()?.trim().toLowerCase();
    if (!current) return data;
    const sorted = [...data.commanders].sort((a, b) => {
      const aMatch = a.name.trim().toLowerCase() === current;
      const bMatch = b.name.trim().toLowerCase() === current;
      if (aMatch && !bMatch) return -1;
      if (!aMatch && bMatch) return 1;
      return 0;
    });
    return { ...data, commanders: sorted };
  });
  protected factionName = computed(() => this.dashboard()?.factionName ?? 'The 501st Guild');

  /** Avatar du CMDR connecté : priorité frontierAuth, sinon depuis la liste des commandeurs (match par nom). */
  protected connectedCmdrAvatar = computed(() => {
    const url = this.frontierAuth.profile()?.avatarUrl;
    if (url) return url;
    const current = this.frontierAuth.commanderName()?.trim().toLowerCase();
    if (!current) return null;
    const cmdr = this.commanders()?.commanders?.find(c => c.name.trim().toLowerCase() === current);
    return cmdr?.avatarUrl ?? null;
  });

  /** Opération Réveil : tous les systèmes non-seed triés par données les plus anciennes en premier (null/inconnu tout en haut). */
  protected reveilSystems = computed((): { id: number; name: string; inaraUrl: string | null; ageDays: number | null }[] => {
    const s = this.guildSystemsSync.systems();
    const all: GuildSystemBgsDto[] = [
      ...(s.origin ?? []),
      ...(s.headquarter ?? []),
      ...(s.surveillance ?? []),
      ...(s.conflicts ?? []),
      ...(s.critical ?? []),
      ...(s.low ?? []),
      ...(s.healthy ?? []),
      ...(s.others ?? []),
    ];
    // Déduplique par nom
    const seen = new Set<string>();
    const unique = all.filter(sys => {
      if (sys.isFromSeed) return false;
      if (seen.has(sys.name)) return false;
      seen.add(sys.name);
      return true;
    });
    // Trie : null/inconnu en premier, puis plus ancien en premier
    return unique
      .map(sys => ({ id: sys.id, name: sys.name, inaraUrl: sys.inaraUrl ?? null, ageDays: inaraAgeDays(sys.lastUpdated) }))
      .filter(item => item.ageDays === null || item.ageDays >= 5)
      .sort((a, b) => {
        if (a.ageDays === null && b.ageDays === null) return 0;
        if (a.ageDays === null) return -1;
        if (b.ageDays === null) return 1;
        return b.ageDays - a.ageDays;
      });
  });

  protected readonly copiedReveilSystemId = signal<number | null>(null);

  protected async copyReveilSystemName(event: Event, name: string, id: number): Promise<void> {
    event.stopPropagation();
    try {
      await navigator.clipboard.writeText(name);
    } catch {
      const ta = document.createElement('textarea');
      ta.value = name;
      ta.style.position = 'fixed';
      ta.style.left = '-9999px';
      document.body.appendChild(ta);
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
    }
    this.copiedReveilSystemId.set(id);
    setTimeout(() => this.copiedReveilSystemId.set(null), 1500);
  }

  protected openReveilSystemInara(inaraUrl: string | null, name: string): void {
    const url = inaraUrl?.trim()
      ? inaraUrl.trim()
      : `https://inara.cz/elite/search/?search=${encodeURIComponent(name)}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  }

  /** Compteurs filtres : design carré (compteur + texte), cliquables. Surveillance : vert si rien à signaler, rouge si au moins un critique. Total = systèmes uniques (surveillance/conflits n'augmentent pas le total). */
  protected mapFilterCounts = computed((): { value: SystemsFilterValue; label: string; count: number; surveillanceHasCritical?: boolean }[] => {
    const s = this.guildSystemsSync.systems();
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
    const uniqueById = new Map<number, typeof allList[0]>();
    for (const sys of allList) {
      if (!uniqueById.has(sys.id)) uniqueById.set(sys.id, sys);
    }
    let withoutNewsCount = 0;
    for (const sys of uniqueById.values()) {
      if (isInaraWithoutNewsCategory(sys)) withoutNewsCount++;
    }
    const totalCount = uniqueById.size;
    const conflictIds = new Set<number>();
    for (const sys of allList) {
      if (hasConflictState(sys)) conflictIds.add(sys.id);
    }
    const conflictsCount = conflictIds.size;
    const surveillanceHasCritical = (s.surveillance ?? []).some((sys) => sys.influencePercent < 5);
    const items: { value: SystemsFilterValue; label: string; count: number; surveillanceHasCritical?: boolean }[] = [
      { value: 'all', label: 'Total', count: totalCount },
      { value: 'critical', label: 'Critiques', count: s.critical?.length ?? 0 },
      { value: 'conflicts', label: 'Conflits', count: conflictsCount },
      { value: 'surveillance', label: 'Surveillance', count: s.surveillance?.length ?? 0, surveillanceHasCritical },
      { value: 'healthy', label: 'Sains', count: s.healthy?.length ?? 0 },
      { value: 'withoutNews', label: 'Sans signal', count: withoutNewsCount },
    ];
    return items;
  });

  /** Compteurs à gauche : Total + rouges (critiques, conflits, surveillance critique). */
  /** Compteurs à gauche : Total, alertes rouges, puis « Sans signal » en dernier (ancienneté Inara > 30 j). */
  protected mapFilterCountsLeft = computed(() =>
    this.mapFilterCounts().filter(
      (fb) =>
        fb.value === 'all' ||
        fb.value === 'critical' ||
        fb.value === 'conflicts' ||
        (fb.value === 'surveillance' && fb.surveillanceHasCritical) ||
        fb.value === 'withoutNews'
    )
  );

  /** Compteurs à droite : verts (sains, surveillance ok). */
  protected mapFilterCountsRight = computed(() =>
    this.mapFilterCounts().filter(
      (fb) =>
        fb.value === 'healthy' ||
        (fb.value === 'surveillance' && !fb.surveillanceHasCritical)
    )
  );

  protected setSystemsFilter(value: SystemsFilterValue): void {
    this.guildSystemsSync.systemsFilter.set(value);
    this.journalMapLayerVisited.set(false);
    this.journalMapLayerDiscovered.set(false);
    this.journalMapLayerFullScan.set(false);
  }

  /** Un seul filtre carte à la fois : changement de vue réinitialise l’autre côté. */
  protected onMapViewFaction(): void {
    this.mapViewMode.set('faction');
    this.journalMapLayerVisited.set(false);
    this.journalMapLayerDiscovered.set(false);
    this.journalMapLayerFullScan.set(false);
  }

  protected onMapViewCmdr(): void {
    this.mapViewMode.set('cmdr');
    this.guildSystemsSync.systemsFilter.set('all');
  }

  protected onMapViewGalacticBridge(): void {
    this.mapViewMode.set('galacticBridge');
    this.guildSystemsSync.systemsFilter.set('all');
    this.journalMapLayerVisited.set(false);
    this.journalMapLayerDiscovered.set(false);
    this.journalMapLayerFullScan.set(false);
  }

  /** Calques journal exclusifs ; reclic sur l’actif désactive. Réinitialise le filtre Faction sur « Total ». */
  protected selectJournalMapLayer(mode: 'visited' | 'discovered' | 'fullscan'): void {
    this.guildSystemsSync.systemsFilter.set('all');
    const vis = this.journalMapLayerVisited();
    const disc = this.journalMapLayerDiscovered();
    const full = this.journalMapLayerFullScan();
    const was =
      (mode === 'visited' && vis) ||
      (mode === 'discovered' && disc) ||
      (mode === 'fullscan' && full);
    if (was) {
      this.journalMapLayerVisited.set(false);
      this.journalMapLayerDiscovered.set(false);
      this.journalMapLayerFullScan.set(false);
      return;
    }
    this.journalMapLayerVisited.set(mode === 'visited');
    this.journalMapLayerDiscovered.set(mode === 'discovered');
    this.journalMapLayerFullScan.set(mode === 'fullscan');
  }

  protected showCmdrConnected = signal(true);

  private addLog(msg: string): void {
    this.syncLog.addLog(msg);
  }

  protected clearLogs(): void {
    this.syncLog.clearLogs();
  }

  protected toggleSyncStatusCollapsed(): void {
    this.syncStatusCollapsed.update((v) => !v);
  }

  protected formatSyncDate(d: Date | null): string {
    if (!d) return '—';
    const dt = d instanceof Date ? d : new Date(d);
    return dt.toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'medium' });
  }

  protected async copyLogsToClipboard(): Promise<void> {
    const text = this.syncLogsWithRecap();
    try {
      await navigator.clipboard.writeText(text);
      this.addLog('Journal copié dans le presse-papier');
    } catch (e) {
      this.addLog('Erreur copie : ' + (e instanceof Error ? e.message : String(e)));
    }
  }

  protected openSettings(): void {
    this.settingsModal?.open();
  }

  protected syncCmdrsTooltip = computed(() => {
    const url = this.guildSettings.inaraSquadronUrl();
    const last = this.guildSettings.lastCommandersSyncAt();
    if (!url) return "Configurer l'URL squadron dans Paramètres";
    return last ? `Dernière sync CMDRs: ${this.formatLastSync(last)}` : 'Jamais synchronisé — Cliquer pour ouvrir Inara';
  });

  protected syncAvatarsRosterTooltip = computed(() => {
    const url = this.guildSettings.inaraSquadronUrl();
    if (!url) return "Configurer l'URL squadron dans Paramètres";
    return "Récupérer les avatars de tous les membres du roster (ouvre chaque page CMDR)";
  });

  protected formatLastSync(iso: string): string {
    const d = new Date(iso);
    return d.toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'medium' });
  }

  /** Affiche une date UTC ISO en heure locale (bloc CMDR — dernière synchro journal). */
  protected formatJournalIsoUtc(iso: string): string {
    return this.formatSyncDate(new Date(iso));
  }

  /** Texte des logs avec récap Inara en tête. */
  protected syncLogsWithRecap = computed(() => {
    const systems = this.lastSystemsSyncError() ?? (this.guildSettings.lastSystemsImportAt() ? this.formatLastSync(this.guildSettings.lastSystemsImportAt()!) : '—');
    const cmdrs = this.lastRosterSyncError() ?? (this.guildSettings.lastCommandersSyncAt() ? this.formatLastSync(this.guildSettings.lastCommandersSyncAt()!) : '—');
    const avatar = this.lastAvatarSyncError() ?? (this.guildSettings.lastAvatarImportAt() ? this.formatLastSync(this.guildSettings.lastAvatarImportAt()!) : '—');
    const globalTs = this.lastInaraSyncTs() ? this.formatLastSync(this.lastInaraSyncTs()!) : null;
    const recap = [
      `Systèmes: ${systems}`,
      `CMDRs: ${cmdrs}`,
      `Avatar: ${avatar}`,
      globalTs ? `Dernière sync globale: ${globalTs}` : null,
    ].filter(Boolean).join('\n');
    const logs = this.syncLog.logsText();
    const logsContent = logs === '(aucun log)' ? '' : logs;
    return recap + (logsContent ? `\n\n${logsContent}` : '');
  });

  /** Lignes du journal : logs inversés (plus récent en haut), ---------, récap en bas. */
  protected syncLogLines = computed(() => {
    const text = this.syncLogsWithRecap() || '(aucun log)';
    const all = text.split('\n');
    const recapEnd = all.findIndex((l) => l.startsWith('['));
    const recapLines = recapEnd >= 0 ? all.slice(0, recapEnd).filter((l) => l.trim() !== '') : all;
    const rawLogLines = recapEnd >= 0 ? all.slice(recapEnd) : [];
    const maxLogLines = 400;
    const logLines =
      rawLogLines.length > maxLogLines
        ? [...rawLogLines.slice(0, maxLogLines), '[… tronqué : trop de lignes de journal …]']
        : rawLogLines;
    const progress = this.systemsImportProgress();
    const journalProgress = this.journalBackfillProgress();
    const progressLine = progress ? [`[${new Date().toISOString().slice(11, 23)}] ${progress}`] : [];
    const journalLine = journalProgress ? [`[${new Date().toISOString().slice(11, 23)}] ${journalProgress}`] : [];
    // Ne pas injecter de faux log « Journal parsé » ici : parsedDaysCount concerne le parseur (bouton Parser → carte),
    // pas la sync brut CAPI ; un new Date() dans ce computed refaisait une ligne à chaque rafraîchissement.
    const logsReversed = [...logLines, ...progressLine, ...journalLine].reverse();
    return [...logsReversed, '---------', ...recapLines];
  });

  /** Déclenche l'enrichissement EDSM après un import réussi, puis poll la progression. */
  private triggerEdsmEnrichment(): void {
    this.guildSystemsApi.enrichEdsm().subscribe({
      next: (res) => {
        if (res.started && res.total) {
          this.addLog(`Enrichissement EDSM démarré (${res.total} système${res.total > 1 ? 's' : ''})`);
          this.startSystemsImportPolling();
        }
      },
      error: (err) => {
        const msg = err?.error?.error ?? err?.error?.message ?? err?.message ?? 'Erreur';
        this.addLog(`EDSM : impossible de démarrer — ${msg}`);
      },
    });
  }

  private startSystemsImportPolling(): void {
    this.stopSystemsImportPolling();
    this.systemsImportPollingRef = setInterval(() => {
      this.guildSystemsApi.getImportProgress().subscribe({
        next: (p) => {
          if (!p.active) return;
          if (p.phase === 'edsm') {
            const statusLabels: Record<string, string> = {
              préparation: 'Préparation des systèmes',
              'requête groupée': 'Requête groupée en cours',
              'réponse reçue': 'Réponse reçue',
              analyse: 'Analyse des résultats',
            };
            const statusLabel = p.status ? statusLabels[p.status] ?? p.status : '';
            const base = `EDSM tendances : requête groupée (${p.current}/${p.total})`;
            this.systemsImportProgress.set(statusLabel ? `${base} — ${statusLabel}` : base);
          } else if (p.phase === 'coords') {
            const base = `EDSM coords : batch (${p.current}/${p.total})`;
            this.systemsImportProgress.set(p.status ? `${base} — ${p.status}` : base);
          } else if (p.phase === 'done') {
            this.stopSystemsImportPolling();
            this.systemsImportProgress.set(null);
            this.addEdsmResultLogs(p.enrichedCount, p.displayableCount, p.ignoredCount, p.coordsEnrichedCount, p.error);
            this.guildSystemsSync.loadSystems();
          }
        },
      });
    }, 400);
  }

  private stopSystemsImportPolling(): void {
    if (this.systemsImportPollingRef != null) {
      clearInterval(this.systemsImportPollingRef);
      this.systemsImportPollingRef = null;
    }
  }

  private addImportSuccessLog(detail: { inserted?: number; updated?: number; total?: number }): void {
    const total = detail.total ?? 0;
    const inserted = detail.inserted ?? 0;
    const updated = detail.updated ?? 0;
    const changed = inserted + updated;
    this.addLog(`Import Inara : ${total} système${total > 1 ? 's' : ''} reçu${total > 1 ? 's' : ''}, ${changed} mis à jour`);
  }

  private addEdsmResultLogs(
    enrichedCount?: number,
    displayableCount?: number,
    ignoredCount?: number,
    coordsEnrichedCount?: number,
    error?: string,
  ): void {
    if (error) {
      this.addLog(`EDSM : erreur — ${error}`);
    } else if (enrichedCount != null) {
      const disp = displayableCount ?? 0;
      const ign = ignoredCount ?? 0;
      const coords = coordsEnrichedCount ?? 0;
      this.addLog(
        `EDSM : ${enrichedCount} système${enrichedCount > 1 ? 's' : ''} enrichi${enrichedCount > 1 ? 's' : ''}, ${disp} tendance${disp > 1 ? 's' : ''} affichable${disp > 1 ? 's' : ''}, ${ign} ignorée${ign > 1 ? 's' : ''} (arrondi à 0,00%), ${coords} coordonnée${coords > 1 ? 's' : ''}`,
      );
    }
  }

  protected isErrorLine(line: string): boolean {
    return line.toLowerCase().includes('erreur');
  }

  /** Horodatage le plus récent parmi les 3 syncs Inara (systèmes, CMDRs, avatar). */
  protected lastInaraSyncTs = computed(() => {
    const arr = [
      this.guildSettings.lastSystemsImportAt(),
      this.guildSettings.lastCommandersSyncAt(),
      this.guildSettings.lastAvatarImportAt(),
    ].filter((s): s is string => !!s);
    if (arr.length === 0) return null;
    return arr.sort((a, b) => new Date(b).getTime() - new Date(a).getTime())[0];
  });

  protected onUpdateScriptClick(): void {
    window.open('/assets/scripts/inara-sync.user.js', '_blank', 'noopener,noreferrer');
  }

  /** Depuis le bloc journal : ouvre le flux OAuth Frontier (même que le menu). */
  protected onConnectFrontierForJournal(): void {
    this.addLog('Connexion Frontier — suivez la fenêtre ou l’onglet pour autoriser l’accès.');
    this.frontierAuth.login();
  }

  protected onSyncJournalClick(): void {
    if (this.journalUnifiedRunning()) return;
    this.frontierJournalApi.startUnifiedSync().subscribe({
      next: (res) => {
        this.addLog(res.message);
        this.startJournalUnifiedPolling(res.message);
      },
      error: (err) => {
        const msg = err?.error?.message ?? err?.message ?? 'Erreur démarrage sync journal';
        this.addLog(msg);
        if (err?.status === 400) this.startJournalUnifiedPolling();
      },
    });
  }

  protected onJournalExportClick(): void {
    this.frontierJournalApi.exportJournalBlob().subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `frontier-journal-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}.zip`;
        a.click();
        URL.revokeObjectURL(url);
        this.addLog('Journal Frontier : export ZIP téléchargé.');
      },
      error: (err) => {
        const msg = err?.error?.message ?? err?.message ?? 'Export impossible';
        this.addLog(`Journal Frontier — export : ${msg}`);
      },
    });
  }

  protected triggerJournalImportReplace(): void {
    this.journalImportPending.set({ strategy: 'replace', duplicatePolicy: 'skip' });
  }

  protected triggerJournalImportMergeSkip(): void {
    this.journalImportPending.set({ strategy: 'merge', duplicatePolicy: 'skip' });
  }

  protected triggerJournalImportMergePreferBackup(): void {
    this.journalImportPending.set({ strategy: 'merge', duplicatePolicy: 'import' });
  }

  protected onJournalImportFileSelected(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    const pending = this.journalImportPending();
    this.journalImportPending.set(null);
    if (!file || !pending) return;

    this.frontierJournalApi.importJournal(file, pending.strategy, pending.duplicatePolicy).subscribe({
      next: (res) => {
        if (res.message) this.addLog(`Journal Frontier — import : ${res.message}`);
        this.refreshJournalParseAndDerived();
      },
      error: (err) => {
        const body = err?.error;
        const msg =
          typeof body?.message === 'string'
            ? body.message
            : typeof body === 'string'
              ? body
              : err?.message ?? 'Import échoué';
        this.addLog(`Journal Frontier — import : ${msg}`);
      },
    });
  }

  private startJournalUnifiedPolling(alreadyLoggedStarter?: string | null): void {
    this.stopJournalUnifiedPolling();
    this.journalUnifiedExpectComplete = true;
    this.journalUnifiedLastLoggedMessage = alreadyLoggedStarter ?? null;
    const tick = () => {
      this.frontierJournalApi.getUnifiedSyncStatus().subscribe({
        next: (st) => {
          this.journalUnifiedStatus.set(st);
          if (st.isRunning) {
            if (st.lastMessage && st.lastMessage !== this.journalUnifiedLastLoggedMessage) {
              this.journalUnifiedLastLoggedMessage = st.lastMessage;
              this.addLog(st.lastMessage);
            }
            if (st.lastMessage) this.journalBackfillProgress.set(st.lastMessage);
            return;
          }
          this.journalBackfillProgress.set(null);
          if (this.journalUnifiedExpectComplete) {
            this.journalUnifiedExpectComplete = false;
            this.stopJournalUnifiedPolling();
            this.journalUnifiedLastLoggedMessage = null;
            if (st.lastError) {
              this.addLog(`Journal Frontier : erreur — ${st.lastError}`);
            } else {
              if (st.lastMessage) this.addLog(st.lastMessage);
              const sm = st.summaryMessage as string | null | undefined;
              if (sm) this.addLog(sm);
            }
            this.refreshJournalParseAndDerived();
          }
        },
      });
    };
    tick();
    this.journalUnifiedPollingRef = setInterval(tick, 2000);
  }

  private stopJournalUnifiedPolling(): void {
    if (this.journalUnifiedPollingRef != null) {
      clearInterval(this.journalUnifiedPollingRef);
      this.journalUnifiedPollingRef = null;
    }
  }

  private loadJournalUnifiedStatus(): void {
    this.frontierJournalApi.getUnifiedSyncStatus().subscribe({
      next: (st) => {
        this.journalUnifiedStatus.set(st);
        if (st.isRunning) {
          if (st.lastMessage) this.journalBackfillProgress.set(st.lastMessage);
          this.startJournalUnifiedPolling();
        }
      },
    });
  }

  private refreshJournalParseAndDerived(): void {
    this.frontierJournalApi.getParseStatus().subscribe({
      next: (st) => this.journalParseStatus.set(st),
    });
    this.loadJournalDerived();
  }

  private loadJournalDerived(): void {
    this.frontierJournalApi.getDerivedSystems().subscribe({
      next: (res) => {
        this.journalDerivedSystemsList.set(res.systems ?? []);
        const map: Record<string, { isVisited: boolean; hasFirstDiscoveryBody: boolean; isFullScanned: boolean }> = {};
        for (const s of res.systems) {
          map[s.systemName.trim().toUpperCase()] = {
            isVisited: s.isVisited,
            hasFirstDiscoveryBody: s.hasFirstDiscoveryBody,
            isFullScanned: s.isFullScanned,
          };
        }
        this.journalDerivedByName.set(map);
      },
      error: () => {
        /* silencieux si pas encore de derived */
      },
    });
  }

  ngOnInit(): void {
    this.addLog('Dashboard initialisé');
    this.addLog('Prêt — utilisez les boutons sync pour importer depuis Inara');
    this.guildSettings.load();
    this.inaraBridge.check();
    this.rareCommodityAlert.checkOncePerDay();
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') this.guildSettings.load();
      });
    }
    if (typeof window !== 'undefined') {
      window.addEventListener('message', (ev: MessageEvent) => {
        if (ev.origin !== 'https://inara.cz') return;
        const d = ev.data;
        if (typeof console !== 'undefined' && console.log) {
          console.log('[Dashboard] postMessage RECU depuis Inara', { origin: ev.origin, type: d?.type, source: d?.source, isObject: !!(d && typeof d === 'object') });
        }
        if (!d || typeof d !== 'object') return;
        const src = d.source as 'systems' | 'roster' | 'avatar';
        const detail = d.detail as { inserted?: number; updated?: number; total?: number; imported?: number; commanderName?: string } | undefined;
        if (d.type === 'inara-sync-started') {
          const labels = { systems: 'systèmes', roster: 'roster', avatar: 'avatar' };
          this.addLog(`Import ${labels[src]} démarré`);
          return;
        }
        if (d.type === 'inara-sync-success') {
          if (typeof console !== 'undefined' && console.log) {
            console.log('[Dashboard] postMessage inara-sync-success RECU — type:', src, '— refresh démarré');
          }
          if (src === 'systems') this.lastSystemsSyncError.set(null);
          if (src === 'roster') this.lastRosterSyncError.set(null);
          if (src === 'avatar') this.lastAvatarSyncError.set(null);
          this.guildSettings.load();
          if (src === 'systems') {
            this.guildSystemsSync.loadSystems();
            this.addImportSuccessLog(detail as { inserted?: number; updated?: number; total?: number });
            this.triggerEdsmEnrichment();
          }
          if (src === 'roster' || src === 'avatar') this.loadCommanders();
          if (src === 'roster' && detail) {
            const n = detail.imported ?? detail.total ?? 0;
            this.addLog(n > 0 ? `${n} CMDR(s) importés` : 'Sync roster réussie');
          } else if (src === 'avatar' && detail?.commanderName) {
            this.addLog(`Avatar mis à jour pour ${detail.commanderName}`);
          } else if (src !== 'systems') {
            this.addLog(`Sync ${src} réussie`);
          }
          this.addLog('Onglet fermé');
          this.addLog('Dashboard rafraîchi');
          if (src === 'avatar' && this.avatarUrlsQueue.length > 0) this.openNextAvatarFromQueue();
          if (typeof console !== 'undefined' && console.log) {
            console.log('[Dashboard] Refresh terminé');
          }
          return;
        }
        if (d.type === 'inara-sync-error') {
          const msg = (d.message as string) || 'Erreur inconnue';
          if (src === 'systems') {
            this.stopSystemsImportPolling();
            this.systemsImportProgress.set(null);
            this.lastSystemsSyncError.set(msg);
          }
          if (src === 'roster') this.lastRosterSyncError.set(msg);
          if (src === 'avatar') this.lastAvatarSyncError.set(msg);
          const labels = { systems: 'Systèmes', roster: 'Roster', avatar: 'Avatar' };
          this.addLog(`Erreur ${labels[src]} : ${msg}`);
          if (src === 'avatar' && this.avatarUrlsQueue.length > 0) this.openNextAvatarFromQueue();
          return;
        }
        if (d.type === 'inara-roster-cmdr-urls') {
          const urls = (d.urls as string[]) || [];
          if (urls.length === 0) {
            this.addLog('Sync avatars roster : aucun lien CMDR trouvé sur la page');
            return;
          }
          this.avatarUrlsQueue = [...urls];
          this.avatarUrlsTotal = urls.length;
          this.addLog(`Sync avatars roster : ${urls.length} membre(s) à traiter`);
          this.openNextAvatarFromQueue();
        }
      });
    }
    setTimeout(() => this.inaraBridge.checkNow(), 800);
    let skipFrontierCheck = false;
    if (typeof window !== 'undefined') {
      const params = new URLSearchParams(window.location.search);
      const frontier = params.get('frontier');
      if (window.opener && (frontier === 'success' || frontier === 'error')) {
        const msg = frontier === 'error' ? (params.get('message') ?? 'Tentative OAuth expirée ou remplacée. Relancez la connexion Frontier.') : null;
        window.opener.postMessage({ type: 'frontier-oauth-done', success: frontier === 'success', message: msg }, window.location.origin);
        window.close();
        return;
      }
      if (frontier === 'success') {
        window.history.replaceState({}, '', window.location.pathname);
      } else if (frontier === 'error') {
        const msg = params.get('message') ?? 'Tentative OAuth expirée ou remplacée. Relancez la connexion Frontier.';
        this.frontierAuth.setError(msg);
        this.addLog('Frontier: ' + msg);
        window.history.replaceState({}, '', window.location.pathname);
        skipFrontierCheck = true;
      }
    }
    if (!skipFrontierCheck) {
      this.frontierAuth.checkAndLoadProfile().subscribe({
        error: () => this.refreshJournalParseAndDerived(),
      });
    }
    this.dashboardApi.getDashboard(null).subscribe({
      next: (d) => this.dashboard.set(d),
      error: (err) => this.addLog('Erreur chargement dashboard : ' + (err?.error?.error ?? err?.message ?? 'Erreur serveur')),
    });
    this.loadCommanders();
  }

  private openNextAvatarFromQueue(): void {
    const url = this.avatarUrlsQueue.shift();
    if (!url) {
      this.addLog('Sync avatars roster : terminé');
      this.loadCommanders();
      return;
    }
    const fullUrl = this.inaraBridge.buildAutoImportUrl(url);
    if (!fullUrl) {
      this.addLog('Erreur build URL pour ' + url);
      this.openNextAvatarFromQueue();
      return;
    }
    this.addLog(`Ouverture page CMDR ${this.avatarUrlsTotal - this.avatarUrlsQueue.length}/${this.avatarUrlsTotal} (avatar)`);
    window.open(fullUrl, '_blank');
  }

  private loadCommanders(): void {
    this.commandersApi.getCommanders().subscribe({
      next: (d) => this.commanders.set(d),
      error: (err) => {
        this.commanders.set({ commanders: [], lastSyncedAt: null, dataSource: 'cached' });
        this.addLog('Erreur chargement CMDRs : ' + (err?.error?.error ?? err?.message ?? 'Erreur serveur'));
      },
    });
  }

  protected onSyncSystemsClick(): void {
    this.addLog('Clic bouton squadron → sync systèmes');
    const url = this.guildSettings.inaraFactionPresenceUrl();
    if (!url) {
      this.addLog('URL faction non configurée — Paramètres');
      this.syncHelpModal.show();
      return;
    }
    if (!this.inaraBridge.openWithAutoImport(url)) {
      this.addLog('Script Inara absent — installez Tampermonkey');
      this.syncHelpModal.show();
      return;
    }
    this.addLog('Ouverture page Inara systems');
  }

  protected onSyncCmdrsClick(): void {
    this.addLog('Clic sync roster → lancement');
    const url = this.guildSettings.inaraSquadronUrl();
    if (!url) {
      this.addLog('URL squadron non configurée — Paramètres');
      this.syncHelpModal.show();
      return;
    }
    if (!this.inaraBridge.openWithAutoImport(url)) {
      this.addLog('Script Inara absent — installez Tampermonkey');
      this.syncHelpModal.show();
      return;
    }
    this.addLog('Ouverture page Inara roster');
  }

  /** File d'attente des URLs CMDR à traiter pour sync avatars roster. */
  private avatarUrlsQueue: string[] = [];
  private avatarUrlsTotal = 0;

  protected onSyncAvatarsRosterClick(): void {
    this.addLog('Clic sync avatars roster → lancement');
    const url = this.guildSettings.inaraSquadronUrl();
    if (!url) {
      this.addLog('URL squadron non configurée — Paramètres');
      this.syncHelpModal.show();
      return;
    }
    if (!this.inaraBridge.openSyncAvatarsRoster(url)) {
      this.addLog('Script Inara absent — installez Tampermonkey');
      this.syncHelpModal.show();
      return;
    }
    this.addLog('Ouverture page Inara roster (extraction liens CMDR)');
  }

  protected onSyncCmdrAvatarClick(): void {
    this.addLog('Clic sync avatar → lancement');
    const url = this.guildSettings.inaraCmdrUrl();
    if (!url) {
      this.addLog('URL CMDR non configurée — Paramètres');
      this.syncHelpModal.show();
      return;
    }
    if (!this.inaraBridge.openWithAutoImport(url)) {
      this.addLog('Script Inara absent — installez Tampermonkey');
      this.syncHelpModal.show();
      return;
    }
    this.addLog('Ouverture page Inara CMDR');
  }

  /** Sync Inara → cache puis recharge les commanders. */
  private refreshDashboard(): void {
    if (this.refreshProgress() > 0) {
      this.addLog('Rafraîchissement déjà en cours — ignoré');
      return;
    }
    this.addLog('Rafraîchissement manuel démarré');
    this.refreshProgress.set(1);
    const start = performance.now();
    const duration = 3000;
    let frameId: number;

    const animate = () => {
      const elapsed = performance.now() - start;
      const p = Math.min(90, (elapsed / duration) * 90);
      this.refreshProgress.set(p);
      if (p < 90) frameId = requestAnimationFrame(animate);
    };
    frameId = requestAnimationFrame(animate);

    this.commandersApi.syncCommanders().subscribe({
      next: (res) => {
        cancelAnimationFrame(frameId);
        this.refreshProgress.set(100);
        this.addLog(res.syncedCount > 0 ? `${res.syncedCount} CMDR(s) synchronisés` : 'Rafraîchissement terminé');
        this.guildSettings.load();
        this.loadCommanders();
        setTimeout(() => this.refreshProgress.set(0), 400);
      },
      error: (err) => {
        cancelAnimationFrame(frameId);
        this.refreshProgress.set(0);
        const msg =
          err?.error?.error ??
          err?.error?.message ??
          err?.message ??
          (err instanceof Error ? err.message : 'Erreur inconnue');
        this.addLog('Erreur rafraîchissement : ' + String(msg).slice(0, 500));
      },
    });
  }

  ngOnDestroy(): void {
    this.stopJournalUnifiedPolling();
  }
}
