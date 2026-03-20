import { Component } from '@angular/core';
import { TruncateTooltipDirective } from '../../shared/directives/truncate-tooltip.directive';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [TruncateTooltipDirective],
  template: `
    <div class="page">
      <div class="page-bg">
        <img class="page-bg-img" src="https://inara.cz/data/gallery/180/180177x4304.jpg" alt="" draggable="false" />
        <div class="page-bg-overlay"></div>
      </div>
      <header class="header-zone">
        <h1 class="title">{{ factionName }}</h1>
        <div class="header-emblem-wrapper">
          <span class="header-emblem-flank header-emblem-flank--left">GALACTIC</span>
          <button type="button" class="emblem-box" (click)="onSquadronSync()"
            truncateTooltip="Synchroniser les données" [truncateTooltipForce]="true" [truncateTooltipAbove]="true">
            <img class="emblem-img" src="assets/squadron-emblem.png" alt="Squadron emblem" />
          </button>
          <span class="header-emblem-flank header-emblem-flank--right">CONTROL</span>
        </div>
      </header>
      <main class="main-grid">
        <aside class="col col-left">
          <div class="box"><h3>Guild Systems</h3></div>
          <div class="box"><h3>Next Galactic Meeting</h3></div>
        </aside>
        <section class="col col-center">
          <div class="map-section">
            <div class="box map-box"><h3>The 501st Guild Map</h3></div>
          </div>
          <div class="box"><h3>Sync Status</h3></div>
          <div class="center-row">
            <div class="box box-no-title"></div>
            <div class="box"><h3>Thargoid War</h3></div>
          </div>
        </section>
        <aside class="col col-right">
          <div class="box"><h3>In Progress Missions</h3></div>
          <div class="box"><h3>Diplomatic Pipeline</h3></div>
          <div class="box"><h3>CMDRs</h3></div>
        </aside>
      </main>
    </div>
  `,
  styles: [`
    .page {
      position: relative;
      display: flex;
      flex-direction: column;
      min-height: 100vh;
      width: 100%;
      max-width: 100vw;
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
      z-index: 1;
      min-height: 104px;
      width: 100%;
      background: rgba(6, 20, 35, 0.88);
      border-bottom: 1px solid rgba(0, 212, 255, 0.35);
      padding: 24px 1rem 2.5rem;
      display: flex;
      align-items: flex-start;
      justify-content: center;
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
      font-size: 0.75rem;
      font-weight: 600;
      color: rgba(255, 255, 255, 0.9);
      text-transform: uppercase;
      letter-spacing: 0.15em;
    }
    .emblem-box {
      width: 72px;
      height: 72px;
      border-radius: 16px;
      border: 1px solid rgba(0, 212, 255, 0.5);
      box-shadow: 0 0 8px rgba(0, 212, 255, 0.4);
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
    .emblem-box:hover {
      transform: scale(1.06);
      border-color: rgba(0, 234, 255, 0.9);
      box-shadow: 0 0 14px rgba(0, 234, 255, 0.6), 0 0 24px rgba(0, 234, 255, 0.3);
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
    .title {
      margin: 0;
      font-size: 26px;
      font-family: 'Orbitron', sans-serif;
      color: #00eaff;
      text-shadow:
        0 0 5px rgba(0, 234, 255, 0.7),
        0 0 10px rgba(0, 234, 255, 0.5),
        0 0 20px rgba(0, 234, 255, 0.3);
    }
    .main-grid {
      position: relative;
      z-index: 1;
      flex: 1;
      min-height: 0;
      display: grid;
      grid-template-columns: minmax(160px, 1fr) minmax(280px, 2.2fr) minmax(160px, 1fr);
      align-items: stretch;
      gap: 1rem;
      width: 100%;
      max-width: 100%;
      margin: 0;
      padding: 64px 1rem 1rem;
    }
    @media (min-width: 900px) {
      .header-zone { padding: 1.5rem 1.5rem 2.5rem; }
      .main-grid {
        gap: 1.5rem;
        padding: 64px 1.5rem 1.5rem;
        max-width: 1600px;
        margin: 0 auto;
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
    }
    .col-left .box,
    .col-right .box {
      flex: 1;
      min-height: 0;
    }
    .box {
      background: rgba(6, 20, 35, 0.88);
      border: 1px solid rgba(0, 212, 255, 0.35);
      border-radius: 16px;
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
  `],
})
export class DashboardComponent {
  protected factionName = 'The 501st Guild';

  protected onSquadronSync(): void {
    // TODO: lancer la synchronisation des données du squadron
  }
}
