import { Injectable, computed, signal } from '@angular/core';

/** Backoff en secondes : 30 → 60 → 120 → 120 → 300 (plafond). */
const BACKOFF_LADDER_SEC = [30, 60, 120, 120, 300];

/**
 * Cooldown client après HTTP 429 CAPI (inventaire logistique) pour ne pas spammer Frontier.
 * Indépendant du Real sync : partagé entre effet inventaire et cycles Real sync.
 */
@Injectable({ providedIn: 'root' })
export class FrontierCapiRateLimitService {
  private readonly cooldownUntilMs = signal(0);
  private streak = 0;

  /** Horodatage fin de cooldown (ms depuis epoch). */
  readonly nextAllowedAtMs = this.cooldownUntilMs.asReadonly();

  /** Secondes restantes avant le prochain GET inventaire autorisé (mis à jour chaque seconde). */
  readonly secondsUntilAllowed = signal(0);

  readonly isInCooldown = computed(() => this.secondsUntilAllowed() > 0);

  constructor() {
    setInterval(() => this.tickCountdown(), 1000);
    this.tickCountdown();
  }

  private tickCountdown(): void {
    const until = this.cooldownUntilMs();
    this.secondsUntilAllowed.set(Math.max(0, Math.ceil((until - Date.now()) / 1000)));
  }

  canFetchInventory(): boolean {
    return Date.now() >= this.cooldownUntilMs();
  }

  /**
   * Après un 429 CAPI (réponse JSON ou HTTP sur GET inventaire).
   * @param suggestedSeconds Retry-After serveur / DTO si présent
   */
  record429(suggestedSeconds?: number | null): void {
    console.debug('[RealSync] HTTP 429 received');
    const idx = Math.min(this.streak, BACKOFF_LADDER_SEC.length - 1);
    this.streak++;
    const ladderSec = BACKOFF_LADDER_SEC[idx];
    const sec =
      suggestedSeconds != null && suggestedSeconds > 0 ? suggestedSeconds : ladderSec;
    const ms = sec * 1000;
    const until = Date.now() + ms;
    this.cooldownUntilMs.set(until);
    console.debug(`[RealSync] applying backoff ${ms} ms`);
    console.debug(`[RealSync] next retry scheduled at ${new Date(until).toISOString()}`);
    this.tickCountdown();
  }

  /** Inventaire reçu sans flag rate limit sur ce cycle. */
  recordSuccess(): void {
    this.streak = 0;
  }
}
