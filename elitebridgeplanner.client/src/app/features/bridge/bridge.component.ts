import { Component, inject, OnInit, computed } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { BridgeStore } from '../../core/services/bridge.store';
import { AuthService } from '../../core/auth/auth.service';
import { RareCommodityAlertService } from '../../core/services/rare-commodity-alert.service';
import { ThemeSelectorComponent } from '../../shared/components/theme-selector/theme-selector.component';
import { ProfileMenuComponent } from '../../shared/components/profile-menu/profile-menu.component';
import { BridgeVisualizerComponent } from './bridge-visualizer/bridge-visualizer.component';
import { SystemListComponent } from './system-list/system-list.component';
import { SystemDetailComponent } from './system-detail/system-detail.component';
import { StationsPanelComponent } from './stations-panel/stations-panel.component';
import { TranslateModule } from '@ngx-translate/core';

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
  readonly rareCommodityAlert = inject(RareCommodityAlertService);
  private readonly route = inject(ActivatedRoute);

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

    // Vérification Inara rare commodities — une fois par jour
    this.rareCommodityAlert.checkOncePerDay();
  }

  backToBridges(): void {
    this.store.clearActiveBridge();
  }

  logout(): void {
    this.authService.logout();
  }
}
