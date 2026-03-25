import { Component, inject, signal, computed, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { catchError, forkJoin, of } from 'rxjs';
import { GuildSettingsService } from '../../../core/services/guild-settings.service';
import { EdsmJournalApiService } from '../../../core/services/edsm-journal-api.service';
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
            <div class="form-group form-group--inara-api">
              <div class="form-label-row">
                <label for="inara-api-key">Inara — Clé API (INAPI)</label>
                <span class="info-i-wrap" [attr.title]="helpInaraApiKey" role="img" [attr.aria-label]="helpInaraApiKey">
                  <svg class="info-i-svg" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path stroke-linecap="round" d="M12 16v-4M12 8h.01"/></svg>
                </span>
              </div>
              <input
                id="inara-api-key"
                type="password"
                class="form-input"
                [class.form-input--error]="inaraApiError()"
                autocomplete="new-password"
                [placeholder]="inaraApiKeyPlaceholder()"
                [(ngModel)]="inaraApiKey"
                name="inaraApiKey"
              />
              @if (inaraApiError()) {
                <span class="form-error">{{ inaraApiError() }}</span>
              }
              <span class="form-hint">
                Sert aux appels inapi/v1 (ex. profil CMDR) et à l’en-tête roster. Laisser vide pour ne pas changer. Fichier serveur (hors git) : Data/inara-api-user.json
              </span>
            </div>
            <div class="form-group form-group--edsm">
              <div class="form-label-row">
                <label for="edsm-cmdr">EDSM — Nom du commandant</label>
                <span class="info-i-wrap" [attr.title]="helpEdsmCommander" role="img" [attr.aria-label]="helpEdsmCommander">
                  <svg class="info-i-svg" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path stroke-linecap="round" d="M12 16v-4M12 8h.01"/></svg>
                </span>
              </div>
              <input
                id="edsm-cmdr"
                type="text"
                class="form-input"
                [class.form-input--error]="edsmError()"
                autocomplete="username"
                placeholder="Exactement comme sur edsm.net"
                [(ngModel)]="edsmCommanderName"
                name="edsmCommanderName"
              />
              <div class="form-label-row label-second">
                <label for="edsm-key">EDSM — Clé API</label>
                <span class="info-i-wrap" [attr.title]="helpEdsmApiKey" role="img" [attr.aria-label]="helpEdsmApiKey">
                  <svg class="info-i-svg" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path stroke-linecap="round" d="M12 16v-4M12 8h.01"/></svg>
                </span>
              </div>
              <input
                id="edsm-key"
                type="password"
                class="form-input"
                [class.form-input--error]="edsmError()"
                autocomplete="new-password"
                [placeholder]="edsmApiKeyPlaceholder()"
                [(ngModel)]="edsmApiKey"
                name="edsmApiKey"
              />
              @if (edsmError()) {
                <span class="form-error">{{ edsmError() }}</span>
              }
              <span class="form-hint">
                Paramètres EDSM →
                <a href="https://www.edsm.net/en_GB/settings/api" target="_blank" rel="noopener noreferrer" class="link-inline">Ma clé API</a>.
                Laisser la clé vide pour ne pas la modifier. Fichier serveur (hors git) : Data/edsm-journal-user.json
              </span>
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
    .form-label-row {
      display: flex;
      align-items: center;
      gap: 0.35rem;
      margin-bottom: 0.35rem;
    }
    .form-label-row label {
      margin-bottom: 0;
    }
    .form-label-row.label-second {
      margin-top: 0.75rem;
    }
    .info-i-wrap {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      cursor: help;
      color: rgba(0, 212, 255, 0.55);
      flex-shrink: 0;
      line-height: 0;
    }
    .info-i-wrap:hover {
      color: rgba(0, 229, 255, 0.9);
    }
    .info-i-svg {
      width: 13px;
      height: 13px;
      display: block;
    }
    .form-group--inara-api {
      margin-top: 1rem;
      padding-top: 1rem;
      border-top: 1px solid rgba(0, 212, 255, 0.14);
    }
    .form-group--edsm {
      margin-top: 1.25rem;
      padding-top: 1rem;
      border-top: 1px solid rgba(0, 255, 200, 0.2);
    }
    .link-inline {
      color: #00e5ff;
      text-decoration: none;
    }
    .link-inline:hover {
      text-decoration: underline;
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
  private readonly edsmJournalApi = inject(EdsmJournalApiService);

  readonly closed = output<void>();
  scriptUrl = '/assets/scripts/inara-sync.user.js';
  readonly visible = signal(false);
  readonly saving = signal(false);
  readonly factionError = signal<string | null>(null);
  readonly squadronError = signal<string | null>(null);
  readonly cmdrError = signal<string | null>(null);
  readonly edsmError = signal<string | null>(null);
  readonly inaraApiError = signal<string | null>(null);
  readonly edsmApiKeyConfigured = signal(false);
  readonly inaraApiKeyConfigured = signal(false);
  readonly edsmApiKeyPlaceholder = computed(() =>
    this.edsmApiKeyConfigured() ? '•••• laisser vide pour conserver la clé enregistrée' : 'Coller la clé API EDSM',
  );
  readonly inaraApiKeyPlaceholder = computed(() =>
    this.inaraApiKeyConfigured() ? '•••• laisser vide pour conserver la clé enregistrée' : 'Coller la clé API Inara (INAPI)',
  );

  /** Textes du survol (i) — pas de secrets. */
  readonly helpInaraApiKey = `Clé API Inara (INAPI, inapi/v1) : utilisée pour les requêtes type getCommanderProfile et pour retenter l’accès à la page roster HTML avec l’en-tête X-Inara-ApiKey. Optionnelle si le roster est public.`;
  readonly helpEdsmCommander = `Nom du commandant exactement comme enregistré sur edsm.net. Requis pour envoyer des lignes du journal de vol vers EDSM.`;
  readonly helpEdsmApiKey = `Clé API EDSM (Paramètres → Ma clé API) : authentifie l’envoi du journal vers Elite Dangerous Star Map (API Journal v1).`;

  factionUrl = '';
  squadronUrl = '';
  cmdrUrl = '';
  inaraApiKey = '';
  edsmCommanderName = '';
  edsmApiKey = '';

  open(): void {
    const s = this.settings.settings();
    this.factionUrl = s?.inaraFactionPresenceUrl ?? '';
    this.squadronUrl = s?.inaraSquadronUrl ?? '';
    this.cmdrUrl = s?.inaraCmdrUrl ?? '';
    this.factionError.set(null);
    this.squadronError.set(null);
    this.cmdrError.set(null);
    this.edsmError.set(null);
    this.inaraApiError.set(null);
    this.edsmApiKey = '';
    this.inaraApiKey = '';
    forkJoin({
      edsm: this.edsmJournalApi.getJournalSettings().pipe(
        catchError(() => of({ commanderName: '', apiKeyConfigured: false })),
      ),
      inaraApi: this.settings.getInaraApiSettings().pipe(catchError(() => of({ apiKeyConfigured: false }))),
    }).subscribe({
      next: ({ edsm, inaraApi }) => {
        this.edsmCommanderName = edsm.commanderName ?? '';
        this.edsmApiKeyConfigured.set(!!edsm.apiKeyConfigured);
        this.inaraApiKeyConfigured.set(!!inaraApi.apiKeyConfigured);
      },
    });
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
    this.edsmError.set(null);
    this.inaraApiError.set(null);

    const payload: GuildSettingsUpdateDto = {
      inaraFactionPresenceUrl: this.factionUrl.trim() || null,
      inaraSquadronUrl: this.squadronUrl.trim() || null,
      inaraCmdrUrl: this.cmdrUrl.trim() || null,
    };

    this.saving.set(true);
    this.settings.update(payload).subscribe({
      next: () => {
        const edsmBody: { commanderName: string; apiKey?: string } = { commanderName: this.edsmCommanderName.trim() };
        const k = this.edsmApiKey.trim();
        if (k) edsmBody.apiKey = k;
        this.edsmJournalApi.putJournalSettings(edsmBody).subscribe({
          next: () => {
            const finish = () => {
              this.saving.set(false);
              this.close();
            };
            const inaraK = this.inaraApiKey.trim();
            if (!inaraK) {
              finish();
              return;
            }
            this.settings.putInaraApiSettings({ apiKey: inaraK }).subscribe({
              next: () => finish(),
              error: (err) => {
                this.saving.set(false);
                const msg = (err?.error?.error ?? err?.error?.message ?? err?.message ?? 'Erreur clé Inara') as string;
                this.inaraApiError.set(msg);
              },
            });
          },
          error: (err) => {
            this.saving.set(false);
            const msg = (err?.error?.error ?? err?.error?.message ?? err?.message ?? 'Erreur EDSM') as string;
            this.edsmError.set(msg);
          },
        });
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
