import { Component, inject, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { BridgeStore } from '../../core/services/bridge.store';
import { AuthService } from '../../core/auth/auth.service';
import { ThemeSelectorComponent } from '../../shared/components/theme-selector/theme-selector.component';
import { ProfileMenuComponent } from '../../shared/components/profile-menu/profile-menu.component';
import { BridgeVisualizerComponent } from './bridge-visualizer/bridge-visualizer.component';
import { SystemListComponent } from './system-list/system-list.component';
import { SystemDetailComponent } from './system-detail/system-detail.component';
import { StationsPanelComponent } from './stations-panel/stations-panel.component';
import { TranslateModule } from '@ngx-translate/core';
import { BridgeRoute501Service } from '../../core/services/bridge-route-501.service';

@Component({
  selector: 'app-bridge',
  standalone: true,
  imports: [
    ThemeSelectorComponent,
    ProfileMenuComponent,
    BridgeVisualizerComponent,
    SystemListComponent,
    SystemDetailComponent,
    StationsPanelComponent,
    TranslateModule
  ],
  templateUrl: './bridge.component.html',
  styleUrl: './bridge.component.scss'
})
export class BridgeComponent implements OnInit {
  readonly store = inject(BridgeStore);
  readonly authService = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly bridgeRoute501 = inject(BridgeRoute501Service);

  /** Envoi route vers le dashboard 501 (carte Pont galactique). */
  readonly sendingTo501 = signal(false);
  readonly send501Message = signal<string | null>(null);

  readonly bridgeLabel = computed(() => {
    const systems = this.store.orderedSystems();
    if (systems.length === 0) return 'Nom du pont';
    const depart = systems[0]?.name?.trim().split(/\s+/)[0] ?? '';
    if (systems.length === 1) return depart ? `Pont ${depart}` : 'Nom du pont';
    const arrivee = systems.at(-1)?.name?.trim().split(/\s+/)[0] ?? '';
    return depart && arrivee ? `Pont ${depart} - ${arrivee}` : depart ? `Pont ${depart}` : 'Nom du pont';
  });

  ngOnInit(): void {
    const bridgeIdParam = this.route.snapshot.queryParams['bridgeId'];
    const bridgeId = bridgeIdParam ? parseInt(bridgeIdParam, 10) : 1;
    this.store.loadBridge(Number.isFinite(bridgeId) ? bridgeId : 1);
  }

  backToBridges(): void {
    this.store.clearActiveBridge();
  }

  logout(): void {
    this.authService.logout();
  }

  sendRouteTo501Dashboard(): void {
    this.send501Message.set(null);
    this.sendingTo501.set(true);
    this.bridgeRoute501.sendActiveBridgeTo501().subscribe({
      next: (r) => {
        this.sendingTo501.set(false);
        this.send501Message.set(`Carte 501 : ${r.pointCount} point(s) envoyé(s). Ouvrez la vue « Pont galactique ».`);
      },
      error: (e: Error) => {
        this.sendingTo501.set(false);
        this.send501Message.set(e?.message ?? 'Envoi vers 501 impossible.');
      },
    });
  }
}
