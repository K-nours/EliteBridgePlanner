import { Component, Input, Output, EventEmitter } from '@angular/core';
import { GuildSystemsMapComponent } from '../guild-systems-map/guild-systems-map.component';
import type { SystemsFilterValue, GuildSystemsResponseDto } from '../../../core/models/guild-systems.model';

type MapFilterItem = { value: SystemsFilterValue; label: string; count: number; surveillanceHasCritical?: boolean };

@Component({
  selector: 'app-map-panel',
  standalone: true,
  imports: [GuildSystemsMapComponent],
  templateUrl: './map-panel.component.html',
  styleUrl: './map-panel.component.scss',
})
export class MapPanelComponent {
  @Input() mapViewMode: 'faction' | 'galacticBridge' = 'faction';
  @Input() systems!: GuildSystemsResponseDto;
  @Input() systemsFilter: SystemsFilterValue = 'all';
  @Input() mapFilterCountsLeft: MapFilterItem[] = [];
  @Input() mapFilterCountsRight: MapFilterItem[] = [];

  @Output() mapViewModeChange = new EventEmitter<'faction' | 'galacticBridge'>();
  @Output() systemsFilterChange = new EventEmitter<SystemsFilterValue>();
}
