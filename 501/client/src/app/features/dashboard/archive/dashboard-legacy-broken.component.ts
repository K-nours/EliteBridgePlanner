import { Component } from '@angular/core';

/**
 * Référence visuelle uniquement — NE PAS MODIFIER.
 * Copie figée du dashboard pour consultation.
 * Route: /archive/legacy-broken
 */
@Component({
  selector: 'app-dashboard-legacy-broken',
  standalone: true,
  template: `
    <div class="dashboard">
      <div class="dashboard-bg"></div>
      <header class="header">
        <div class="header-inner">
          <h1 class="header-title">THE 501ST GUILD</h1>
          <div class="header-logo">
            <span class="logo-text">GALACTIC</span>
            <span class="logo-line"></span>
            <div class="emblem" role="img" aria-label="Squadron emblem">
              <img src="assets/squadron-emblem.png" alt="" />
            </div>
            <span class="logo-line"></span>
            <span class="logo-text">CONTROL</span>
          </div>
        </div>
      </header>
      <main class="main">
        <aside class="col col-left">
          <section class="panel guild-systems">
            <h2 class="panel-title">THE 501ST GUILD SYSTEMS</h2>
            <div class="systems-list"></div>
          </section>
          <section class="panel">
            <h2 class="panel-title">NEXT GALACTIC MEETING</h2>
            <p class="meeting-desc">Rixe contre l'AEP dimanche soir!</p>
            <p class="meeting-date">23/03/2026 16:25</p>
            <div class="attendance">
              <span class="attendance-item accepted">K-nour: Accepted</span>
              <span class="attendance-item waiting">Bib0x0n0x: Waiting</span>
            </div>
          </section>
        </aside>
        <section class="col col-center">
          <section class="panel panel-expansion">
            <h2 class="panel-title">EXPANSION TARGETS</h2>
            <p class="expansion-content">Système candidat à expansion</p>
          </section>
          <div class="stats-row">
            <div class="stat-box">
              <span class="stat-value">6</span>
              <span class="stat-label">SYSTEMS NUMBER</span>
            </div>
            <div class="stat-box">
              <span class="stat-value">3</span>
              <span class="stat-label">CLEAN SYSTEMS NUMBER</span>
            </div>
          </div>
          <section class="panel">
            <div class="panel-header">
              <h2 class="panel-title">SYNC STATUS</h2>
              <button type="button" class="refresh-btn" title="Rafraîchir">↻</button>
            </div>
            <div class="sync-list">
              <div class="sync-row"><span>Dernière sync globale</span></div>
              <div class="sync-row"><span>EDSM</span></div>
              <div class="sync-row"><span>Inara</span></div>
              <div class="sync-row"><span>Statut</span><span class="sync-value">never</span></div>
              <div class="sync-row"><span>Systèmes</span><span class="sync-value">6</span></div>
              <div class="sync-row"><span>Factions</span><span class="sync-value">8</span></div>
              <div class="sync-row"><span>Squadron</span><span class="sync-value">0</span></div>
            </div>
          </section>
          <section class="panel">
            <h2 class="panel-title">THARGOID WAR</h2>
            <p class="empty">Mise à jour guerre Thargoïd à venir.</p>
          </section>
        </section>
        <aside class="col col-right">
          <section class="panel">
            <h2 class="panel-title">IN PROGRESS MISSIONS</h2>
            <div class="mission-list">
              <div class="mission-item">
                <span class="mission-title">Renforcer influence Hip4794</span>
                <span class="mission-assignee">K-nour</span>
              </div>
              <div class="mission-item">
                <span class="mission-title">Surveiller Achuar</span>
                <span class="mission-assignee">Bib0x0n0x</span>
              </div>
            </div>
          </section>
          <section class="panel">
            <h2 class="panel-title">DIPLOMATIC PIPELINE</h2>
            <div class="diplo-list">
              <div class="diplo-item hostile">AEP — Hostile</div>
              <div class="diplo-item">Alliance des indépendants — Negotiating</div>
            </div>
          </section>
          <section class="panel">
            <h2 class="panel-title">CMDRS</h2>
            <div class="cmdrs-row">
              <div class="cmdr-avatar">B</div>
              <div class="cmdr-avatar">B</div>
              <div class="cmdr-avatar">K</div>
              <div class="cmdr-avatar">K</div>
              <div class="cmdr-avatar">W</div>
            </div>
            <div class="cmdrs-names">
              <span>BamBam</span>
              <span>Bib0x0n0x</span>
              <span>K-mousse</span>
              <span>K-nour</span>
              <span>Wilddog</span>
            </div>
          </section>
        </aside>
      </main>
    </div>
  `,
  styles: [`
    .dashboard { position: relative; min-height: 100vh; color: #e6edf3; display: flex; flex-direction: column; overflow-x: hidden; }
    .dashboard-bg {
      position: fixed; inset: 0; z-index: 0;
      background: radial-gradient(ellipse at 30% 20%, rgba(0, 80, 120, 0.15) 0%, transparent 50%),
                  radial-gradient(ellipse at 70% 80%, rgba(0, 212, 255, 0.08) 0%, transparent 40%),
                  linear-gradient(180deg, #060a0f 0%, #0a1220 50%, #060a0f 100%);
    }
    .header, .main { position: relative; z-index: 1; }
    .header {
      padding: 1.25rem 1rem; display: flex; justify-content: center; align-items: center;
      background: rgba(6, 10, 15, 0.6); backdrop-filter: blur(8px);
      font-family: 'Orbitron', sans-serif;
    }
    .header-inner {
      display: flex; flex-direction: column; align-items: center;
      gap: 0.75rem;
    }
    .header-title {
      margin: 0; font-size: clamp(1.75rem, 4.5vw, 2.5rem); font-weight: 800; color: #00ccff;
      letter-spacing: 0.18em; text-transform: uppercase;
      text-shadow: 0 0 20px rgba(0, 204, 255, 0.6), 0 0 40px rgba(0, 204, 255, 0.3);
      line-height: 1.1;
    }
    .header-logo {
      display: flex; align-items: center; justify-content: center; gap: 0;
    }
    .logo-text {
      font-size: 0.75rem; font-weight: 600; color: #00ccff;
      letter-spacing: 0.25em; text-transform: uppercase;
      text-shadow: 0 0 10px rgba(0, 204, 255, 0.5);
      padding: 0 0.75rem;
    }
    .logo-line {
      width: 3rem; height: 1px; background: #00ccff;
      box-shadow: 0 0 6px rgba(0, 204, 255, 0.5);
    }
    .emblem {
      width: 56px; height: 56px;
      border-radius: 50%; border: 2px solid #d4af37; overflow: hidden;
      box-shadow: 0 0 12px rgba(212, 175, 55, 0.4), inset 0 0 20px rgba(0,0,0,0.4);
      flex-shrink: 0;
    }
    .emblem img {
      width: 100%; height: 100%; object-fit: cover; display: block;
    }
    .stats-row { display: flex; gap: 1rem; }
    .stat-box {
      flex: 1; border: 1px solid rgba(0, 212, 255, 0.25); border-radius: 8px;
      padding: 0.75rem; text-align: center; background: rgba(18, 24, 32, 0.6);
      box-shadow: 0 0 10px rgba(0, 212, 255, 0.08);
    }
    .stat-value {
      display: block; font-size: 1.5rem; font-weight: 700; color: #00d4ff;
      text-shadow: 0 0 10px rgba(0, 212, 255, 0.4);
    }
    .stat-label {
      font-size: 0.65rem; text-transform: uppercase; letter-spacing: 1px;
      opacity: 0.8; margin-top: 0.2rem;
    }
    .main {
      flex: 1; padding: 1rem; display: grid;
      grid-template-columns: minmax(200px, 1fr) minmax(400px, 2.2fr) minmax(200px, 1fr); gap: 1rem; min-width: 0;
      max-width: 1600px; margin: 0 auto; width: 100%;
    }
    .col { display: flex; flex-direction: column; gap: 1rem; min-width: 0; }
    .panel-expansion { border-color: rgba(0, 212, 255, 0.35); }
    .expansion-content { margin: 0; font-size: 0.9rem; color: #00d4ff; text-shadow: 0 0 8px rgba(0, 212, 255, 0.3); }
    .panel {
      background: rgba(18, 24, 32, 0.85); border: 1px solid rgba(0, 212, 255, 0.25);
      border-radius: 8px; padding: 1rem; backdrop-filter: blur(4px);
    }
    .panel-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
    .panel-title { font-size: 0.75rem; font-weight: 600; text-transform: uppercase; letter-spacing: 1.5px; margin: 0; color: #00d4ff; }
    .empty { font-size: 0.85rem; opacity: 0.6; margin: 0; }
    .refresh-btn { background: none; border: none; color: #00d4ff; cursor: pointer; font-size: 1.2rem; padding: 0.2rem; opacity: 0.8; }
    .refresh-btn:hover { opacity: 1; }
    .meeting-desc { margin: 0 0 0.25rem 0; font-size: 0.9rem; }
    .meeting-date { margin: 0 0 0.5rem 0; font-size: 0.8rem; opacity: 0.9; }
    .attendance { display: flex; flex-direction: column; gap: 0.25rem; }
    .attendance-item { font-size: 0.8rem; }
    .attendance-item.accepted { color: #00ff88; }
    .attendance-item.waiting { color: #e6edf3; }
    .sync-list { display: flex; flex-direction: column; gap: 0.35rem; }
    .sync-row { display: flex; justify-content: space-between; font-size: 0.85rem; }
    .sync-value { opacity: 0.8; }
    .mission-list { display: flex; flex-direction: column; gap: 0.5rem; }
    .mission-item { display: flex; flex-direction: column; gap: 0.15rem; font-size: 0.85rem; }
    .mission-title { font-weight: 500; }
    .mission-assignee { font-size: 0.75rem; opacity: 0.8; }
    .diplo-list { display: flex; flex-direction: column; gap: 0.35rem; font-size: 0.85rem; }
    .diplo-item.hostile { color: #ff4444; }
    .cmdrs-row { display: flex; gap: 0.5rem; margin-bottom: 0.5rem; }
    .cmdr-avatar {
      width: 36px; height: 36px; border-radius: 50%;
      background: rgba(0, 212, 255, 0.2); border: 1px solid rgba(0, 212, 255, 0.5);
      display: flex; align-items: center; justify-content: center;
      font-size: 0.9rem; font-weight: 600; color: #00d4ff;
    }
    .cmdrs-names { display: flex; flex-wrap: wrap; gap: 0.5rem 1rem; font-size: 0.75rem; opacity: 0.9; }
  `],
})
export class DashboardLegacyBrokenComponent {}
