import { Component, Input, Output, EventEmitter } from '@angular/core';
import { GuildSystemsMapComponent } from '../guild-systems-map/guild-systems-map.component';
import type { SystemsFilterValue, GuildSystemsResponseDto } from '../../../core/models/guild-systems.model';
import type { FrontierJournalSystemDerivedDto } from '../../../core/services/frontier-journal-api.service';

type MapFilterItem = { value: SystemsFilterValue; label: string; count: number; surveillanceHasCritical?: boolean };

@Component({
  selector: 'app-map-panel',
  standalone: true,
  imports: [GuildSystemsMapComponent],
  templateUrl: './map-panel.component.html',
  styleUrl: './map-panel.component.scss',
})
export class MapPanelComponent {
  @Input() mapViewMode: 'faction' | 'cmdr' | 'galacticBridge' = 'faction';
  @Input() journalCmdrMapPoints: FrontierJournalSystemDerivedDto[] = [];
  @Input() systems!: GuildSystemsResponseDto;
  @Input() systemsFilter: SystemsFilterValue = 'all';
  @Input() journalLayerVisited = false;
  @Input() journalLayerDiscovered = false;
  @Input() journalLayerFullScan = false;
  @Input() journalDerivedByName: Record<string, { isVisited: boolean; hasFirstDiscoveryBody: boolean; isFullScanned: boolean }> = {};
  @Input() mapFilterCountsLeft: MapFilterItem[] = [];
  @Input() mapFilterCountsRight: MapFilterItem[] = [];
  @Input() journalCmdrCountVisited = 0;
  @Input() journalCmdrCountDiscovered = 0;
  @Input() journalCmdrCountFullScan = 0;

  @Output() mapViewModeChange = new EventEmitter<'faction' | 'cmdr' | 'galacticBridge'>();
  @Output() systemsFilterChange = new EventEmitter<SystemsFilterValue>();
  @Output() journalLayerChange = new EventEmitter<'visited' | 'discovered' | 'fullscan'>();
}
