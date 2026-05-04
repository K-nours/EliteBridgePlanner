import { Component, inject, OnInit } from '@angular/core';
import { BridgeStore } from '../../core/services/bridge.store';
import { AuthService } from '../../core/auth/auth.service';
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

  ngOnInit(): void {
    this.store.loadBridge(1);
  }

  logout(): void {
    this.authService.logout();
  }
}
