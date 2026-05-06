import { Injectable } from '@angular/core';

/** Après erreur « pas de bloc chantier CAPI » sur refresh-one : évite de spammer la CAPI avec le même marketId. */
const COOLDOWN_MS = 15 * 60 * 1000;

@Injectable({ providedIn: 'root' })
export class ChantierMarketRefreshCooldownService {
  private readonly untilByChantierId = new Map<number, number>();

  recordMarketNoCapiBlock(chantierId: number): void {
    this.untilByChantierId.set(chantierId, Date.now() + COOLDOWN_MS);
    console.debug(
      `[Logistics] refresh-one cooldown ${COOLDOWN_MS} ms for chantierId=${chantierId} (marketId à réenregistrer)`,
    );
  }

  clear(chantierId: number): void {
    this.untilByChantierId.delete(chantierId);
  }

  canPostRefreshOne(chantierId: number): boolean {
    const u = this.untilByChantierId.get(chantierId);
    return u == null || Date.now() >= u;
  }

  secondsUntilRefreshAllowed(chantierId: number): number {
    const u = this.untilByChantierId.get(chantierId);
    if (u == null) return 0;
    return Math.max(0, Math.ceil((u - Date.now()) / 1000));
  }
}
