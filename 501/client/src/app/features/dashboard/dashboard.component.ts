import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { TruncateTooltipDirective } from '../../shared/directives/truncate-tooltip.directive';
import { DataSourceBadgeComponent } from '../../shared/components/data-source-badge/data-source-badge.component';
import { GuildSystemsPanelComponent } from './guild-systems-panel/guild-systems-panel.component';
import { DashboardApiService } from '../../core/services/dashboard-api.service';
import { CommandersApiService } from '../../core/services/commanders-api.service';
import { SyncLogService } from '../../core/services/sync-log.service';
import { GuildSystemsSyncService } from '../../core/services/guild-systems-sync.service';
import { FrontierAuthService } from '../../core/services/frontier-auth.service';
import type { DashboardResponseDto } from '../../core/models/dashboard.model';
import type { CommandersResponseDto } from '../../core/models/commanders.model';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [TruncateTooltipDirective, DataSourceBadgeComponent, GuildSystemsPanelComponent],
  template: `
    <div class="page">
      <div class="page-bg">
        <img class="page-bg-img" src="https://inara.cz/data/gallery/180/180177x4304.jpg" alt="" draggable="false" />
        <div class="page-bg-overlay"></div>
      </div>
      <header class="header-zone">
        <h1 class="title">{{ factionName() }}</h1>
        <div class="header-emblem-wrapper">
          <span class="header-emblem-flank header-emblem-flank--left">GALACTIC</span>
          <div class="emblem-box-wrapper">
            @if (refreshProgress() > 0) {
            <svg class="emblem-progress" viewBox="0 0 72 72">
              <circle class="emblem-progress-bg" cx="36" cy="36" r="34" fill="none" stroke-width="2" />
              <circle class="emblem-progress-fill" cx="36" cy="36" r="34" fill="none" stroke-width="2"
                [attr.stroke-dasharray]="strokeCircumference"
                [attr.stroke-dashoffset]="strokeDashOffset()" />
            </svg>
          }
            <button type="button" class="emblem-box" (click)="onSquadronSync()" [disabled]="refreshProgress() > 0"
              truncateTooltip="Synchroniser les données" [truncateTooltipForce]="true" [truncateTooltipAbove]="true">
              <img class="emblem-img" src="assets/squadron-emblem.png" alt="Squadron emblem" />
            </button>
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
                  @if (frontierAuth.profile()?.avatarUrl; as url) {
                    <img [src]="url" [alt]="frontierAuth.commanderName() ?? 'CMDR'" referrerpolicy="no-referrer" />
                  } @else {
                    <span class="frontier-cmdr-initial">{{ (frontierAuth.commanderName() ?? 'C').charAt(0).toUpperCase() }}</span>
                  }
                </div>
              </div>
              @if (frontierMenuOpen()) {
                <div class="frontier-menu">
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
            <button type="button" class="btn-frontier-login" (click)="frontierAuth.login()">Login Frontier</button>
          }
        </div>
      </header>
      <main class="main-grid">
        <aside class="col col-left">
          <app-guild-systems-panel />
          <div class="box"><h3>Next Galactic Meeting</h3></div>
        </aside>
        <section class="col col-center">
          <div class="map-section">
            <div class="box map-box"><h3>The 501st Guild Map</h3></div>
          </div>
          <div class="box box-sync-status">
            <div class="sync-status-header">
              <h3>Sync Status</h3>
              <div class="sync-buttons">
                <button type="button" class="btn-copy" (click)="copyLogsToClipboard()" [disabled]="syncLog.logs().length === 0">Copy to clipboard</button>
                <button type="button" class="btn-clear" (click)="clearLogs()" [disabled]="syncLog.logs().length === 0">Clear log</button>
              </div>
            </div>
            <div class="sync-status-bgs">
              <div class="sync-status-row">
                <span class="sync-status-label">Dernière tentative</span>
                <span class="sync-status-value">{{ formatSyncDate(guildSystemsSync.lastAttemptAt()) }}</span>
              </div>
              <div class="sync-status-row">
                <span class="sync-status-label">Dernier succès</span>
                <span class="sync-status-value">{{ formatSyncDate(guildSystemsSync.lastSuccessfulSyncAt()) }}</span>
              </div>
              @if (guildSystemsSync.lastSuccessfulSyncAt()) {
                <div class="sync-status-row">
                  <span class="sync-status-label">Systèmes mis à jour</span>
                  <span class="sync-status-value">{{ guildSystemsSync.lastSystemsUpdated() }}</span>
                </div>
              }
              @if (guildSystemsSync.lastErrorMessage()) {
                <div class="sync-status-row sync-status-row--error">
                  <span class="sync-status-label">Erreur</span>
                  <span class="sync-status-value">{{ guildSystemsSync.lastErrorMessage() }}</span>
                </div>
              }
            </div>
            <pre class="sync-logs">{{ syncLog.logsText() }}</pre>
          </div>
          <div class="center-row">
            <div class="box box-no-title"></div>
            <div class="box"><h3>Thargoid War</h3></div>
          </div>
        </section>
        <aside class="col col-right">
          @if (dashboard()?.frontierProfile; as fp) {
          <div class="box box-frontier-cmdr">
            <h3>CMDR connecté</h3>
            <div class="frontier-cmdr-info">
              <span class="frontier-cmdr-name">{{ fp.commanderName }}</span>
              @if (fp.squadronName) { <span class="frontier-cmdr-detail">Squadron: {{ fp.squadronName }}</span> }
              @if (fp.lastSystemName) { <span class="frontier-cmdr-detail">Système: {{ fp.lastSystemName }}</span> }
              @if (fp.shipName) { <span class="frontier-cmdr-detail">Vaisseau: {{ fp.shipName }}</span> }
            </div>
          </div>
          }
          <div class="box"><h3>In Progress Missions</h3></div>
          <div class="box"><h3>Diplomatic Pipeline</h3></div>
          <div class="box box-cmdrs">
            <div class="box-cmdrs-header">
              <h3>CMDRs</h3>
              @if (commanders(); as data) {
                <app-data-source-badge [source]="data.dataSource"
                  [tooltip]="data.lastSyncedAt ? 'Dernière sync: ' + data.lastSyncedAt : ''" />
              }
            </div>
            @if (commanders(); as data) {
            @if (data.commanders.length > 0) {
            <div class="cmdrs-list">
              @for (cmdr of data.commanders; track cmdr.name) {
                <div class="cmdr-item">
                  <div class="cmdr-avatar">
                    @if (cmdr.avatarUrl) {
                      <img [src]="cmdr.avatarUrl" [alt]="cmdr.name" referrerpolicy="no-referrer" />
                    } @else {
                      <span class="cmdr-initial">{{ cmdr.name.charAt(0) }}</span>
                    }
                  </div>
                  <span class="cmdr-name">{{ cmdr.name }}</span>
                  @if (cmdr.role) {
                    <span class="cmdr-role">{{ cmdr.role }}</span>
                  }
                </div>
              }
            </div>
          } @else {
            <div class="cmdrs-empty">Aucun CMDR. Cliquez sur l'écusson pour synchroniser.</div>
          }
          } @else {
            <div class="cmdrs-empty">Chargement...</div>
          }
          </div>
        </aside>
      </main>
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
      min-height: 104px;
      width: 100%;
      background: rgba(6, 20, 35, 0.88);
      border-bottom: 1px solid rgba(0, 212, 255, 0.22);
      padding: 24px 1rem 2.5rem;
      display: flex;
      align-items: flex-start;
      justify-content: center;
    }
    .header-frontier {
      position: absolute;
      right: 1rem;
      top: 50%;
      transform: translateY(-50%);
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
      width: 28px;
      height: 28px;
      border-radius: 50%;
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
    .frontier-menu-item:hover {
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
      bottom: 0;
      left: 50%;
      transform: translate(-50%, 50%);
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 1rem;
    }
    .header-emblem-flank {
      font-family: 'Orbitron', sans-serif;
      font-size: 0.55rem;
      font-weight: 600;
      color: rgba(255, 255, 255, 0.9);
      text-transform: uppercase;
      letter-spacing: 0.15em;
    }
    .title {
      margin: 0;
      font-size: 30px;
      font-family: 'Orbitron', sans-serif;
      color: #00eaff;
      text-shadow:
        0 0 5px rgba(0, 234, 255, 0.45),
        0 0 10px rgba(0, 234, 255, 0.3),
        0 0 20px rgba(0, 234, 255, 0.18);
    }
    .emblem-box-wrapper {
      position: relative;
      display: inline-block;
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
      align-items: stretch;
      gap: 1rem;
      width: 100%;
      max-width: 100%;
      margin: 0;
      padding: 64px 1rem 1rem;
      box-sizing: border-box;
    }
    @media (min-width: 1200px) {
      .header-zone { padding: 1.5rem 1.5rem 2.5rem; }
      .main-grid {
        width: 100%;
        grid-template-columns: minmax(200px, 400px) minmax(0, 1fr) minmax(200px, 400px);
        gap: 1.5rem;
        padding: 64px 1.5rem 1.5rem;
        box-sizing: border-box;
      }
    }
    @media (min-width: 900px) and (max-width: 1199px) {
      .header-zone { padding: 1.5rem 1.5rem 2.5rem; }
      .main-grid {
        gap: 1.5rem;
        padding: 64px 1.5rem 1.5rem;
      }
    }
    @media (max-width: 768px) {
      .main-grid {
        grid-template-columns: 1fr;
      }
      .box-row,
      .center-row {
        grid-template-columns: 1fr;
      }
    }
    .col {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      min-width: 0;
    }
    .col-center {
      width: 100%;
    }
    .col-center .map-section,
    .col-center .box-sync-status,
    .col-center .center-row,
    .col-center .box {
      width: 100%;
    }
    .col-left .box,
    .col-right .box {
      flex: 1 1 auto;
      min-height: 0;
    }
    .box {
      background: rgba(6, 20, 35, 0.88);
      border: 1px solid rgba(0, 212, 255, 0.14);
      border-radius: 16px;
      box-shadow:
        0 0 5px rgba(0, 234, 255, 0.03),
        0 0 10px rgba(0, 234, 255, 0.02),
        0 0 20px rgba(0, 234, 255, 0.01);
      padding: 1rem 1.25rem;
      min-height: 80px;
    }
    .box h3 {
      margin: 0;
      font-family: 'Orbitron', sans-serif;
      font-size: 0.75rem;
      font-weight: 600;
      color: #00d4ff;
      text-transform: uppercase;
      letter-spacing: 0.1em;
    }
    .box-sync-status {
      display: flex;
      flex-direction: column;
      min-height: 120px;
    }
    .sync-status-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
      margin-bottom: 0.5rem;
    }
    .sync-status-header h3 {
      margin: 0;
    }
    .btn-copy {
      padding: 0.35rem 0.6rem;
      font-size: 0.65rem;
      font-family: 'Orbitron', sans-serif;
      background: rgba(0, 212, 255, 0.2);
      border: 1px solid rgba(0, 212, 255, 0.4);
      color: #00d4ff;
      border-radius: 4px;
      cursor: pointer;
      flex-shrink: 0;
    }
    .btn-copy:hover:not(:disabled) {
      background: rgba(0, 212, 255, 0.3);
    }
    .btn-copy:disabled,
    .btn-clear:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
    .sync-buttons {
      display: flex;
      gap: 0.5rem;
    }
    .btn-clear {
      padding: 0.35rem 0.6rem;
      font-size: 0.65rem;
      font-family: 'Orbitron', sans-serif;
      background: rgba(255, 100, 100, 0.2);
      border: 1px solid rgba(255, 100, 100, 0.4);
      color: #ff6b6b;
      border-radius: 4px;
      cursor: pointer;
    }
    .btn-clear:hover:not(:disabled) {
      background: rgba(255, 100, 100, 0.3);
    }
    .sync-status-bgs {
      font-family: 'Exo 2', sans-serif;
      font-size: 0.7rem;
      margin-bottom: 0.75rem;
      padding-bottom: 0.75rem;
      border-bottom: 1px solid rgba(0, 212, 255, 0.1);
    }
    .sync-status-row {
      display: flex;
      justify-content: space-between;
      gap: 1rem;
      margin-bottom: 0.25rem;
    }
    .sync-status-row:last-child {
      margin-bottom: 0;
    }
    .sync-status-label {
      color: rgba(255, 255, 255, 0.6);
      flex-shrink: 0;
    }
    .sync-status-value {
      color: rgba(255, 255, 255, 0.9);
      text-align: right;
      word-break: break-word;
    }
    .sync-status-row--error .sync-status-value {
      color: #ff6b6b;
    }
    .sync-logs {
      flex: 1;
      margin: 0;
      font-size: 0.65rem;
      color: rgba(255, 255, 255, 0.8);
      font-family: monospace;
      white-space: pre-wrap;
      word-break: break-all;
      overflow-y: auto;
      max-height: 140px;
      padding: 0.5rem;
      background: rgba(0, 0, 0, 0.3);
      border-radius: 4px;
    }
    .center-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 1rem;
    }
    .map-section {
      position: relative;
    }
    .map-box {
      min-height: 420px;
    }
    .frontier-cmdr-info {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      margin-top: 0.75rem;
      font-family: 'Exo 2', sans-serif;
      font-size: 0.7rem;
    }
    .frontier-cmdr-name {
      font-weight: 600;
      color: #00d4ff;
    }
    .frontier-cmdr-detail {
      color: rgba(255, 255, 255, 0.85);
    }
    .box-cmdrs-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
    }
    .box-cmdrs-header h3 {
      margin: 0;
    }
    .cmdrs-empty {
      font-size: 0.7rem;
      color: rgba(255,255,255,0.5);
      margin-top: 0.5rem;
    }
    .cmdr-role {
      font-size: 0.55rem;
      color: rgba(0, 212, 255, 0.7);
    }
    .box-cmdrs .cmdrs-list {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
      margin-top: 0.75rem;
    }
    .cmdr-item {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.35rem;
    }
    .cmdr-item.is-current .cmdr-avatar {
      box-shadow: 0 0 10px rgba(0, 234, 255, 0.6);
      border-color: rgba(0, 234, 255, 0.9);
    }
    .cmdr-avatar {
      width: 40px;
      height: 40px;
      border-radius: 50%;
      background: rgba(0, 212, 255, 0.2);
      border: 1px solid rgba(0, 212, 255, 0.4);
      display: flex;
      align-items: center;
      justify-content: center;
      overflow: hidden;
    }
    .cmdr-avatar img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .cmdr-initial {
      font-family: 'Orbitron', sans-serif;
      font-size: 1rem;
      font-weight: 700;
      color: #00d4ff;
    }
    .cmdr-name {
      font-size: 0.65rem;
      color: rgba(255, 255, 255, 0.85);
      text-align: center;
      max-width: 60px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `],
})
export class DashboardComponent implements OnInit {
  private readonly dashboardApi = inject(DashboardApiService);
  private readonly commandersApi = inject(CommandersApiService);
  protected readonly frontierAuth = inject(FrontierAuthService);
  protected readonly frontierMenuOpen = signal(false);
  protected readonly syncLog = inject(SyncLogService);
  protected readonly guildSystemsSync = inject(GuildSystemsSyncService);

  protected readonly strokeCircumference = 2 * Math.PI * 34;
  protected refreshProgress = signal(0);
  protected strokeDashOffset = computed(() => this.strokeCircumference * (1 - this.refreshProgress() / 100));

  protected dashboard = signal<DashboardResponseDto | null>(null);
  protected commanders = signal<CommandersResponseDto | null>(null);
  protected factionName = computed(() => this.dashboard()?.factionName ?? 'The 501st Guild');

  private addLog(msg: string): void {
    this.syncLog.addLog(msg);
  }

  protected clearLogs(): void {
    this.syncLog.clearLogs();
  }

  protected formatSyncDate(d: Date | null): string {
    if (!d) return '—';
    const dt = d instanceof Date ? d : new Date(d);
    return dt.toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'medium' });
  }

  protected async copyLogsToClipboard(): Promise<void> {
    const text = this.syncLog.logsText();
    try {
      await navigator.clipboard.writeText(text);
      this.addLog('→ Copié dans le presse-papier');
    } catch (e) {
      this.addLog('→ Erreur copie: ' + (e instanceof Error ? e.message : String(e)));
    }
  }

  ngOnInit(): void {
    this.addLog('Dashboard initialisé');
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
    if (!skipFrontierCheck) this.frontierAuth.checkAndLoadProfile();
    this.dashboardApi.getDashboard(null).subscribe({
      next: (d) => this.dashboard.set(d),
      error: (err) => this.addLog('Erreur dashboard: ' + (err?.message || err?.error?.message || JSON.stringify(err))),
    });
    this.loadCommanders();
  }

  private loadCommanders(): void {
    this.commandersApi.getCommanders().subscribe({
      next: (d) => this.commanders.set(d),
      error: (err) => {
        this.commanders.set({ commanders: [], lastSyncedAt: null, dataSource: 'cached' });
        this.addLog('Erreur commanders: ' + (err?.message || err?.error?.message || JSON.stringify(err)));
      },
    });
  }

  protected onSquadronSync(): void {
    this.addLog('Clic sur bouton squadron');
    this.guildSystemsSync.sync();
    this.refreshDashboard();
  }

  /** Sync Inara → cache puis recharge les commanders. */
  private refreshDashboard(): void {
    if (this.refreshProgress() > 0) {
      this.addLog('Refresh déjà en cours, ignoré');
      return;
    }
    this.addLog('Démarrage refresh...');
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
        this.addLog('Sync OK: ' + res.syncedCount + ' CMDRs');
        this.loadCommanders();
        setTimeout(() => this.refreshProgress.set(0), 400);
      },
      error: (err) => {
        cancelAnimationFrame(frameId);
        this.refreshProgress.set(0);
        const msg = err?.error?.error ?? err?.error?.message ?? err?.message ?? JSON.stringify(err);
        this.addLog('Erreur sync: ' + msg);
      },
    });
  }
}
