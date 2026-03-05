import { Component, inject, OnInit } from '@angular/core';
import { BridgeStore } from '../../core/services/bridge.store';
import { AuthService } from '../../core/auth/auth.service';
import { ThemeSelectorComponent } from '../../shared/components/theme-selector/theme-selector.component';
import { BridgeVisualizerComponent } from './bridge-visualizer/bridge-visualizer.component';
import { SystemListComponent } from './system-list/system-list.component';
import { SystemDetailComponent } from './system-detail/system-detail.component';

@Component({
  selector: 'app-bridge',
  standalone: true,
  imports: [
    ThemeSelectorComponent,
    BridgeVisualizerComponent,
    SystemListComponent,
    SystemDetailComponent
  ],
  templateUrl: './bridge.component.html',
  styleUrl: './bridge.component.scss'
})
export class BridgeComponent implements OnInit {
  readonly store = inject(BridgeStore);
  readonly authService = inject(AuthService);

  ngOnInit(): void {       
    // TODO : ajouter la sélection de pont quand plusieurs ponts seront supportés
    // Pour l'instant on charge directement le pont 1 après le chargement de la liste
    this.store.loadBridge(1);
  }

  logout(): void {
    this.authService.logout();
  }
}
