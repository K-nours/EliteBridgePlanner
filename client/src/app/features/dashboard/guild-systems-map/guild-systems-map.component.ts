import {
  Component,
  Input,
  OnDestroy,
  OnInit,
  AfterViewInit,
  ElementRef,
  ViewChild,
  OnChanges,
  SimpleChanges,
} from '@angular/core';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import type { GuildSystemBgsDto } from '../../../core/models/guild-systems.model';

export type MapCategoryKey =
  | 'origin'
  | 'headquarter'
  | 'surveillance'
  | 'conflicts'
  | 'critical'
  | 'low'
  | 'healthy'
  | 'others';

const CATEGORY_COLORS: Record<MapCategoryKey, number> = {
  origin: 0x00d4ff,
  headquarter: 0xd4af37,
  surveillance: 0x93c5fd,
  conflicts: 0xff6b6b,
  critical: 0xff6b6b,
  low: 0xe0e0e0,
  healthy: 0x00ff88,
  others: 0xaaaaaa,
};

interface MapSystem extends GuildSystemBgsDto {
  mapCategory: MapCategoryKey;
}

@Component({
  selector: 'app-guild-systems-map',
  standalone: true,
  templateUrl: './guild-systems-map.component.html',
  styleUrl: './guild-systems-map.component.scss',
})
export class GuildSystemsMapComponent implements OnInit, AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('canvasContainer', { static: true }) canvasContainerRef!: ElementRef<HTMLDivElement>;

  @Input() systems: GuildSystemsResponseInput = emptyInput();
  @Input() selectedSystemId: number | null = null;

  private scene: THREE.Scene | null = null;
  private camera: THREE.PerspectiveCamera | null = null;
  private renderer: THREE.WebGLRenderer | null = null;
  private controls: OrbitControls | null = null;
  private raycaster = new THREE.Raycaster();
  private mouse = new THREE.Vector2();
  private meshMap = new Map<number, THREE.Mesh>();
  private animationId = 0;
  private hoveredSystem: MapSystem | null = null;

  tooltipData: { system: MapSystem; x: number; y: number } | null = null;
  selectedSystem: MapSystem | null = null;

  /** true si des systèmes existent mais aucun n'a de coords. */
  hasNoCoords(): boolean {
    const list = this.systemsWithCoords;
    const hasAny = this.totalSystemsCount > 0;
    return hasAny && list.length === 0;
  }

  private get totalSystemsCount(): number {
    const s = this.systems;
    const ids = new Set<number>();
    for (const arr of [s.origin, s.headquarter, s.surveillance, s.conflicts, s.critical, s.low, s.healthy, s.others]) {
      for (const sys of arr ?? []) ids.add(sys.id);
    }
    return ids.size;
  }

  private get systemsWithCoords(): MapSystem[] {
    const byId = new Map<number, MapSystem>();
    const append = (arr: GuildSystemBgsDto[], cat: MapCategoryKey) => {
      for (const s of arr ?? []) {
        if (s.coordsX != null && s.coordsY != null && s.coordsZ != null && !byId.has(s.id)) {
          byId.set(s.id, { ...s, mapCategory: cat });
        }
      }
    };
    append(this.systems.origin, 'origin');
    append(this.systems.headquarter, 'headquarter');
    append(this.systems.surveillance, 'surveillance');
    append(this.systems.conflicts, 'conflicts');
    append(this.systems.critical, 'critical');
    append(this.systems.low, 'low');
    append(this.systems.healthy, 'healthy');
    append(this.systems.others, 'others');
    return Array.from(byId.values());
  }

  ngOnInit(): void {}

  ngAfterViewInit(): void {
    this.initScene();
    this.animate();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['systems'] && this.scene) {
      this.updatePoints();
    }
  }

  ngOnDestroy(): void {
    cancelAnimationFrame(this.animationId);
    this.controls?.dispose();
    this.renderer?.dispose();
  }

  private initScene(): void {
    const container = this.canvasContainerRef.nativeElement;
    const width = container.clientWidth;
    const height = container.clientHeight;

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x06141f);

    this.camera = new THREE.PerspectiveCamera(50, width / height, 0.1, 100000);
    this.camera.position.set(500, 500, 500);

    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setSize(width, height);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    container.innerHTML = '';
    container.appendChild(this.renderer.domElement);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.05;

    this.renderer.domElement.addEventListener('mousemove', (e) => this.onMouseMove(e));
    this.renderer.domElement.addEventListener('click', (e) => this.onClick(e));

    window.addEventListener('resize', () => this.onResize());
    this.updatePoints();
  }

  private updatePoints(): void {
    if (!this.scene || !this.camera || !this.controls) return;

    this.meshMap.forEach((m) => this.scene!.remove(m));
    this.meshMap.clear();

    const list = this.systemsWithCoords;
    if (list.length === 0) return;

    let sumX = 0, sumY = 0, sumZ = 0;
    for (const sys of list) {
      sumX += sys.coordsX!;
      sumY += sys.coordsY!;
      sumZ += sys.coordsZ!;
    }
    const n = list.length;
    const cx = sumX / n;
    const cy = sumY / n;
    const cz = sumZ / n;

    for (const sys of list) {
      const geometry = new THREE.SphereGeometry(this.radiusFromInfluence(sys.influencePercent), 8, 6);
      const color = CATEGORY_COLORS[sys.mapCategory] ?? 0xffffff;
      const material = new THREE.MeshBasicMaterial({
        color,
        transparent: true,
        opacity: 0.9,
      });
      const mesh = new THREE.Mesh(geometry, material);
      mesh.position.set(sys.coordsX!, sys.coordsY!, sys.coordsZ!);
      (mesh as THREE.Mesh & { systemData?: MapSystem }).systemData = sys;
      mesh.name = `sys-${sys.id}`;
      this.scene.add(mesh);
      this.meshMap.set(sys.id, mesh);
    }

    this.controls.target.set(cx, cy, cz);
    let maxDist = 0;
    for (const sys of list) {
      const d = Math.hypot(sys.coordsX! - cx, sys.coordsY! - cy, sys.coordsZ! - cz);
      if (d > maxDist) maxDist = d;
    }
    const dist = Math.max(200, maxDist * 1.5);
    this.camera.position.set(cx + dist, cy + dist, cz + dist);
  }

  private radiusFromInfluence(pct: number): number {
    const min = 2;
    const max = 12;
    return min + (pct / 100) * (max - min);
  }

  private onMouseMove(event: MouseEvent): void {
    if (!this.renderer || !this.camera) return;
    const rect = this.renderer.domElement.getBoundingClientRect();
    this.mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    this.mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;

    this.raycaster.setFromCamera(this.mouse, this.camera);
    const meshes = Array.from(this.meshMap.values());
    const hits = this.raycaster.intersectObjects(meshes);

    const hit = hits[0];
    const sys: MapSystem | null = hit?.object
      ? ((hit.object as THREE.Mesh & { systemData?: MapSystem }).systemData ?? null)
      : null;

    if (sys !== this.hoveredSystem) {
      this.hoveredSystem = sys;
      if (sys) {
        this.tooltipData = { system: sys, x: event.clientX, y: event.clientY };
      } else {
        this.tooltipData = null;
      }
    } else if (sys) {
      this.tooltipData = { system: sys, x: event.clientX, y: event.clientY };
    }
  }

  private onClick(_event: MouseEvent): void {
    if (!this.raycaster || !this.camera) return;
    this.raycaster.setFromCamera(this.mouse, this.camera);
    const meshes = Array.from(this.meshMap.values());
    const hits = this.raycaster.intersectObjects(meshes);
    const hit = hits[0];
    const sys: MapSystem | null = hit?.object
      ? ((hit.object as THREE.Mesh & { systemData?: MapSystem }).systemData ?? null)
      : null;
    this.selectedSystem = sys;
  }

  private onResize(): void {
    const container = this.canvasContainerRef.nativeElement;
    const w = container.clientWidth;
    const h = container.clientHeight;
    if (this.camera) {
      (this.camera as THREE.PerspectiveCamera).aspect = w / h;
      this.camera.updateProjectionMatrix();
    }
    this.renderer?.setSize(w, h);
  }

  private animate = (): void => {
    this.animationId = requestAnimationFrame(this.animate);
    this.controls?.update();
    this.renderer?.render(this.scene!, this.camera!);
  };

  hasSignificantDelta(delta?: number | null): boolean {
    if (delta == null) return false;
    return Math.round(delta * 100) !== 0;
  }

  formatDelta(delta?: number | null): string {
    if (delta == null) return '';
    const r = Math.round(delta * 100) / 100;
    if (r === 0) return '';
    const sign = r >= 0 ? '+' : '-';
    return ` ${sign} ${Math.abs(r).toFixed(2).replace('.', ',')}%`;
  }
}

function emptyInput(): GuildSystemsResponseInput {
  return {
    origin: [],
    headquarter: [],
    surveillance: [],
    conflicts: [],
    critical: [],
    low: [],
    healthy: [],
    others: [],
  };
}

export interface GuildSystemsResponseInput {
  origin: GuildSystemBgsDto[];
  headquarter: GuildSystemBgsDto[];
  surveillance: GuildSystemBgsDto[];
  conflicts: GuildSystemBgsDto[];
  critical: GuildSystemBgsDto[];
  low: GuildSystemBgsDto[];
  healthy: GuildSystemBgsDto[];
  others: GuildSystemBgsDto[];
}
