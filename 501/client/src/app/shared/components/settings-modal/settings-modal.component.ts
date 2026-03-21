import { Component, inject, signal, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { GuildSettingsService } from '../../../core/services/guild-settings.service';
import type { GuildSettingsUpdateDto } from '../../../core/models/guild-settings.model';

@Component({
  selector: 'app-settings-modal',
  standalone: true,
  template: `
    @if (visible()) {
      <div class="modal-backdrop" (click)="close()"></div>
      <div class="modal" role="dialog" aria-modal="true" aria-labelledby="settings-title">
        <div class="modal-header">
          <h2 id="settings-title" class="modal-title">Paramètres</h2>
          <button type="button" class="modal-close" (click)="close()" aria-label="Fermer">×</button>
        </div>
        <div class="modal-body">
          <form (ngSubmit)="save()" #form="ngForm">
            <div class="form-group">
              <label for="faction-url">URL page présence faction Inara</label>
              <input
                id="faction-url"
                type="url"
                class="form-input"
                [class.form-input--error]="factionError()"
                placeholder="https://inara.cz/elite/minorfaction-presence/78866/"
                [(ngModel)]="factionUrl"
                name="factionUrl"
              />
              @if (factionError()) {
                <span class="form-error">{{ factionError() }}</span>
              }
            </div>
            <div class="form-group">
              <label for="squadron-url">URL roster squadron Inara</label>
              <input
                id="squadron-url"
                type="url"
                class="form-input"
                [class.form-input--error]="squadronError()"
                placeholder="https://inara.cz/elite/squadron-roster/7158/"
                [(ngModel)]="squadronUrl"
                name="squadronUrl"
              />
              @if (squadronError()) {
                <span class="form-error">{{ squadronError() }}</span>
              }
            </div>
            <div class="form-group">
              <label for="cmdr-url">URL page CMDR Inara</label>
              <input
                id="cmdr-url"
                type="url"
                class="form-input"
                [class.form-input--error]="cmdrError()"
                placeholder="https://inara.cz/elite/cmdr/12345/"
                [(ngModel)]="cmdrUrl"
                name="cmdrUrl"
              />
              @if (cmdrError()) {
                <span class="form-error">{{ cmdrError() }}</span>
              }
              <span class="form-hint">Pour récupérer l'avatar de votre profil Inara</span>
            </div>
            <div class="form-group form-group--script">
              <a [href]="scriptUrl" target="_blank" rel="noopener noreferrer" class="link-script-update">
                Mettre à jour le script Inara Sync
              </a>
              <span class="form-hint">Ouvre le script dans un nouvel onglet — Tampermonkey propose de le réinstaller ou mettre à jour</span>
            </div>
            <div class="modal-actions">
              <button type="button" class="btn btn-secondary" (click)="close()">Annuler</button>
              <button type="submit" class="btn btn-primary" [disabled]="saving()">Enregistrer</button>
            </div>
          </form>
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
    .form-group {
      margin-bottom: 1rem;
    }
    .form-group label {
      display: block;
      font-family: 'Exo 2', sans-serif;
      font-size: 0.75rem;
      color: rgba(255, 255, 255, 0.8);
      margin-bottom: 0.35rem;
    }
    .form-input {
      width: 100%;
      padding: 0.5rem 0.75rem;
      background: rgba(0, 0, 0, 0.3);
      border: 1px solid rgba(0, 212, 255, 0.2);
      border-radius: 6px;
      color: #fff;
      font-size: 0.85rem;
    }
    .form-input::placeholder {
      color: rgba(255, 255, 255, 0.35);
    }
    .form-input--error {
      border-color: #ff6b6b;
    }
    .form-error {
      display: block;
      font-size: 0.7rem;
      color: #ff6b6b;
      margin-top: 0.25rem;
    }
    .form-hint {
      display: block;
      font-size: 0.65rem;
      color: rgba(255, 255, 255, 0.5);
      margin-top: 0.25rem;
    }
    .form-group--script {
      margin-top: 1.25rem;
      padding-top: 1rem;
      border-top: 1px solid rgba(0, 212, 255, 0.14);
    }
    .link-script-update {
      display: inline-block;
      font-family: 'Exo 2', sans-serif;
      font-size: 0.9rem;
      color: #00d4ff;
      text-decoration: none;
    }
    .link-script-update:hover {
      color: #00e5ff;
      text-decoration: underline;
    }
    .modal-actions {
      display: flex;
      justify-content: flex-end;
      gap: 0.75rem;
      margin-top: 1.25rem;
    }
    .btn {
      padding: 0.5rem 1rem;
      border-radius: 6px;
      font-size: 0.85rem;
      cursor: pointer;
      border: none;
    }
    .btn-primary {
      background: #00d4ff;
      color: #060a0f;
    }
    .btn-primary:hover:not(:disabled) {
      background: #00e5ff;
    }
    .btn-secondary {
      background: rgba(255, 255, 255, 0.1);
      color: rgba(255, 255, 255, 0.9);
    }
  `],
  imports: [FormsModule],
})
export class SettingsModalComponent {
  private readonly settings = inject(GuildSettingsService);

  readonly closed = output<void>();
  scriptUrl = '/assets/scripts/inara-sync.user.js';
  readonly visible = signal(false);
  readonly saving = signal(false);
  readonly factionError = signal<string | null>(null);
  readonly squadronError = signal<string | null>(null);
  readonly cmdrError = signal<string | null>(null);

  factionUrl = '';
  squadronUrl = '';
  cmdrUrl = '';

  open(): void {
    const s = this.settings.settings();
    this.factionUrl = s?.inaraFactionPresenceUrl ?? '';
    this.squadronUrl = s?.inaraSquadronUrl ?? '';
    this.cmdrUrl = s?.inaraCmdrUrl ?? '';
    this.factionError.set(null);
    this.squadronError.set(null);
    this.cmdrError.set(null);
    this.visible.set(true);
  }

  close(): void {
    this.visible.set(false);
    this.closed.emit();
  }

  save(): void {
    this.factionError.set(null);
    this.squadronError.set(null);
    this.cmdrError.set(null);

    const payload: GuildSettingsUpdateDto = {
      inaraFactionPresenceUrl: this.factionUrl.trim() || null,
      inaraSquadronUrl: this.squadronUrl.trim() || null,
      inaraCmdrUrl: this.cmdrUrl.trim() || null,
    };

    this.saving.set(true);
    this.settings.update(payload).subscribe({
      next: () => {
        this.saving.set(false);
        this.close();
      },
      error: (err) => {
        this.saving.set(false);
        const msg = (err?.error?.error ?? err?.error?.message ?? err?.message ?? 'Erreur lors de l\'enregistrement') as string;
        if (msg.toLowerCase().includes('squadron')) this.squadronError.set(msg);
        else if (msg.toLowerCase().includes('cmdr')) this.cmdrError.set(msg);
        else if (msg.toLowerCase().includes('faction') || msg.toLowerCase().includes('minorfaction')) this.factionError.set(msg);
        else this.factionError.set(msg);
      },
    });
  }
}
