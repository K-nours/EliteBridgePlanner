import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-sync-help-modal',
  standalone: true,
  template: `
    @if (visible()) {
    <div class="modal-backdrop" (click)="close()"></div>
    <div class="modal" role="dialog" aria-modal="true" aria-labelledby="sync-help-title">
      <div class="modal-header">
        <h2 id="sync-help-title" class="modal-title">Script de synchronisation indisponible</h2>
        <button type="button" class="modal-close" (click)="close()" aria-label="Fermer">×</button>
      </div>
      <div class="modal-body">
        <p class="modal-text">
          Le script de synchronisation n'est pas actif sur cette page. Pour synchroniser avec Inara,
          vous devez l'installer et l'activer.
        </p>
        <ol class="modal-steps">
          <li>Installez l'extension <strong>Tampermonkey</strong> dans votre navigateur (Chrome, Firefox, Edge).</li>
          <li>Ajoutez le script unique fourni par le squadron (fichier <code>inara-sync.user.js</code>) — il gère systèmes et CMDRs selon la page Inara.</li>
          <li>Vérifiez que le script est activé pour <strong>ce dashboard</strong> et pour <strong>inara.cz</strong>.</li>
        </ol>
        <p class="modal-text">
          Consultez la documentation pour plus de détails :
        </p>
        <a [href]="docUrl" target="_blank" rel="noopener noreferrer" class="modal-link">Documentation Inara Sync</a>
        <div class="modal-actions">
          <a [href]="scriptUrl" target="_blank" rel="noopener noreferrer" class="btn btn-primary">Installer le script</a>
          <a [href]="scriptUrl" download="inara-sync.user.js" class="btn btn-download">Télécharger</a>
          <button type="button" class="btn btn-secondary" (click)="close()">Compris</button>
        </div>
      </div>
    </div>
    }
  `,
  styles: [`
    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      z-index: 1000;
    }
    .modal {
      position: fixed;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      background: rgba(6, 20, 35, 0.98);
      border: 1px solid rgba(0, 212, 255, 0.3);
      border-radius: 12px;
      min-width: 420px;
      max-width: 90vw;
      max-height: 90vh;
      overflow-y: auto;
      z-index: 1001;
      box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
    }
    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 1rem 1.25rem;
      border-bottom: 1px solid rgba(0, 212, 255, 0.14);
    }
    .modal-title {
      margin: 0;
      font-family: 'Orbitron', sans-serif;
      font-size: 1rem;
      font-weight: 600;
      color: #00d4ff;
    }
    .modal-close {
      background: none;
      border: none;
      color: rgba(255, 255, 255, 0.7);
      font-size: 1.5rem;
      cursor: pointer;
      line-height: 1;
      padding: 0 0.25rem;
    }
    .modal-close:hover {
      color: #fff;
    }
    .modal-body {
      padding: 1.25rem;
    }
    .modal-text {
      font-family: 'Exo 2', sans-serif;
      font-size: 0.9rem;
      color: rgba(255, 255, 255, 0.9);
      margin: 0 0 1rem;
      line-height: 1.5;
    }
    .modal-steps {
      margin: 0 0 1rem;
      padding-left: 1.5rem;
      font-family: 'Exo 2', sans-serif;
      font-size: 0.85rem;
      color: rgba(255, 255, 255, 0.9);
      line-height: 1.6;
    }
    .modal-steps li {
      margin-bottom: 0.5rem;
    }
    .modal-steps code {
      font-size: 0.8em;
      background: rgba(0, 212, 255, 0.15);
      padding: 0.15rem 0.35rem;
      border-radius: 4px;
    }
    .modal-link {
      display: inline-block;
      font-family: 'Exo 2', sans-serif;
      font-size: 0.9rem;
      color: #00d4ff;
      margin-bottom: 1rem;
      text-decoration: underline;
    }
    .modal-link:hover {
      color: #00e5ff;
    }
    .modal-actions {
      display: flex;
      gap: 0.75rem;
      margin-top: 1rem;
    }
    .btn-download {
      display: inline-block;
      padding: 0.5rem 1rem;
      font-size: 0.85rem;
      font-family: 'Exo 2', sans-serif;
      background: rgba(0, 212, 255, 0.2);
      border: 1px solid rgba(0, 212, 255, 0.4);
      color: #00d4ff;
      border-radius: 6px;
      text-decoration: none;
      cursor: pointer;
      transition: background 0.2s;
    }
    .btn-download:hover {
      background: rgba(0, 212, 255, 0.3);
    }
    .btn {
      display: inline-block;
      padding: 0.5rem 1rem;
      border-radius: 6px;
      font-size: 0.85rem;
      font-family: 'Exo 2', sans-serif;
      cursor: pointer;
      border: none;
      text-decoration: none;
    }
    .btn-primary {
      background: #00d4ff;
      color: #060a0f;
    }
    .btn-primary:hover {
      background: #00e5ff;
    }
    .btn-secondary {
      background: transparent;
      color: rgba(255, 255, 255, 0.8);
      border: 1px solid rgba(255, 255, 255, 0.3);
    }
    .btn-secondary:hover {
      background: rgba(255, 255, 255, 0.08);
    }
  `],
})
export class SyncHelpModalComponent {
  readonly visible = input<boolean>(false);
  readonly closed = output<void>();
  /** URL vers la doc (servie depuis /docs/ en build). */
  docUrl = '/docs/IMPORT-INARA-USERSCRIPT.md';
  /** URL publique du script (ouverture directe → Tampermonkey propose l'installation). */
  scriptUrl = '/assets/scripts/inara-sync.user.js';

  close(): void {
    this.closed.emit();
  }
}
