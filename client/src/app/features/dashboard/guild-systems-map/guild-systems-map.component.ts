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
  inject,
  effect,
} from '@angular/core';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import type { GuildSystemBgsDto } from '../../../core/models/guild-systems.model';
import type { SystemsFilterValue } from '../../../core/models/guild-systems.model';
import { GuildSystemsSyncService } from '../../../core/services/guild-systems-sync.service';

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

const GLOW_CATEGORIES: Set<MapCategoryKey> = new Set(['origin', 'headquarter', 'critical']);

interface MapSystem extends GuildSystemBgsDto {
  mapCategory: MapCategoryKey;
}

const FILTER_TO_CATEGORY: Partial<Record<SystemsFilterValue, MapCategoryKey>> = {
  origin: 'origin',
  hq: 'headquarter',
  surveillance: 'surveillance',
  conflicts: 'conflicts',
  critical: 'critical',
  low: 'low',
  healthy: 'healthy',
  others: 'others',
};

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
  @Input() systemsFilter: SystemsFilterValue = 'all';

  private readonly guildSync = inject(GuildSystemsSyncService);

  private scene: THREE.Scene | null = null;
  private camera: THREE.PerspectiveCamera | null = null;
  private renderer: THREE.WebGLRenderer | null = null;
  private controls: OrbitControls | null = null;
  private raycaster = new THREE.Raycaster();
  private mouse = new THREE.Vector2();
  private meshMap = new Map<number, THREE.Mesh>();
  private animationId = 0;
  private hoveredSystem: MapSystem | null = null;

  /** Vue globale : barycentre + distance. Pour reset. */
  private viewCenter = new THREE.Vector3();
  private viewDistance = 300;

  tooltipData: { system: MapSystem; x: number; y: number } | null = null;
  selectedSystem: MapSystem | null = null;

  /** Zoom 0..100 : 0 = loin, 100 = proche. */
  zoomLevel = 50;

  private panInterval: ReturnType<typeof setInterval> | null = null;

  constructor() {
    effect(() => {
      const id = this.guildSync.focusOnSystemId();
      if (id != null && this.camera && this.controls) this.focusOnSystemId(id);
    });
  }

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
    if (changes['systems'] && this.scene) this.updatePoints();
    if (changes['systemsFilter'] && this.scene) this.updateFilterHighlight();
  }

  ngOnDestroy(): void {
    this.stopPan();
    cancelAnimationFrame(this.animationId);
    this.controls?.dispose();
    this.renderer?.dispose();
  }

  private initScene(): void {
    const container = this.canvasContainerRef.nativeElement;
    const width = container.clientWidth;
    const height = container.clientHeight;

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x030a12);
    this.scene.fog = new THREE.FogExp2(0x030a12, 0.00008);

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
    this.controls.minDistance = 40;
    this.controls.maxDistance = 2000;

    this.addStarfield();

    this.renderer.domElement.addEventListener('mousemove', (e) => this.onMouseMove(e));
    this.renderer.domElement.addEventListener('click', (e) => this.onClick(e));

    window.addEventListener('resize', () => this.onResize());
    this.updatePoints();
  }

  private addStarfield(): void {
    if (!this.scene) return;
    const count = 800;
    const positions = new Float32Array(count * 3);
    const size = 8000;
    for (let i = 0; i < count * 3; i += 3) {
      positions[i] = (Math.random() - 0.5) * size;
      positions[i + 1] = (Math.random() - 0.5) * size;
      positions[i + 2] = (Math.random() - 0.5) * size;
    }
    const geo = new THREE.BufferGeometry().setAttribute('position', new THREE.BufferAttribute(positions, 3));
    const mat = new THREE.PointsMaterial({
      color: 0xffffff,
      size: 0.8,
      transparent: true,
      opacity: 0.4,
    });
    const stars = new THREE.Points(geo, mat);
    this.scene.add(stars);
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
      const r = this.radiusFromInfluence(sys.influencePercent);
      const geometry = new THREE.SphereGeometry(r, 8, 6);
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

      if (GLOW_CATEGORIES.has(sys.mapCategory)) {
        const glowGeom = new THREE.SphereGeometry(r * 1.8, 8, 6);
        const glowMat = new THREE.MeshBasicMaterial({
          color,
          transparent: true,
          opacity: 0.15,
        });
        const glow = new THREE.Mesh(glowGeom, glowMat);
        glow.position.copy(mesh.position);
        this.scene.add(glow);
      }
    }
    this.updateFilterHighlight();

    this.viewCenter.set(cx, cy, cz);
    this.controls.target.copy(this.viewCenter);
    let maxDist = 0;
    for (const sys of list) {
      const d = Math.hypot(sys.coordsX! - cx, sys.coordsY! - cy, sys.coordsZ! - cz);
      if (d > maxDist) maxDist = d;
    }
    this.viewDistance = Math.max(200, maxDist * 1.5);
    this.camera.position.set(cx + this.viewDistance, cy + this.viewDistance, cz + this.viewDistance);
  }

  private radiusFromInfluence(pct: number): number {
    const min = 2;
    const max = 12;
    return min + (pct / 100) * (max - min);
  }

  private updateFilterHighlight(): void {
    if (!this.scene) return;
    const filter = this.systemsFilter;
    const highlightCat = filter === 'all' ? null : (FILTER_TO_CATEGORY[filter] ?? null);
    for (const [, mesh] of this.meshMap) {
      const mat = mesh.material as THREE.MeshBasicMaterial;
      const sys = (mesh as THREE.Mesh & { systemData?: MapSystem }).systemData;
      if (!sys) continue;
      const match = !highlightCat || sys.mapCategory === highlightCat;
      mat.opacity = match ? 0.95 : 0.2;
    }
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
    if (sys) {
      this.guildSync.onMapSystemClicked(sys.name, sys.id);
    } else {
      this.guildSync.clearMapSelection();
    }
  }

  private focusOnSystemId(id: number): void {
    const mesh = this.meshMap.get(id);
    if (!mesh || !this.camera || !this.controls) return;
    const pos = mesh.position.clone();
    this.controls.target.copy(pos);
    const dist = 80;
    this.camera.position.set(pos.x + dist, pos.y + dist, pos.z + dist);
    this.guildSync.focusOnSystemId.set(null);
  }

  resetView(): void {
    if (!this.camera || !this.controls) return;
    this.zoomLevel = 50;
    this.controls.target.copy(this.viewCenter);
    this.camera.position.set(
      this.viewCenter.x + this.viewDistance,
      this.viewCenter.y + this.viewDistance,
      this.viewCenter.z + this.viewDistance
    );
  }

  panCamera(dx: number, dy: number, dz: number): void {
    if (!this.camera || !this.controls) return;
    const d = 50;
    this.controls.target.add(new THREE.Vector3(dx * d, dy * d, dz * d));
    this.camera.position.add(new THREE.Vector3(dx * d, dy * d, dz * d));
  }

  startPan(ev: PointerEvent, dx: number, dy: number, dz: number): void {
    this.stopPan();
    (ev.currentTarget as HTMLElement)?.setPointerCapture(ev.pointerId);
    this.panInterval = setInterval(() => this.panCamera(dx, dy, dz), 50);
  }

  stopPan(): void {
    if (this.panInterval) {
      clearInterval(this.panInterval);
      this.panInterval = null;
    }
  }

  zoomStep(delta: number): void {
    this.setZoomFromLevel(this.zoomLevel + delta);
  }

  setZoomFromLevel(level: number): void {
    this.zoomLevel = Math.max(0, Math.min(100, level));
    if (!this.camera || !this.controls) return;
    const dir = new THREE.Vector3().subVectors(this.camera.position, this.controls.target).normalize();
    const minD = 50;
    const maxD = 800;
    const dist = minD + (1 - this.zoomLevel / 100) * (maxD - minD);
    this.camera.position.copy(this.controls.target).add(dir.multiplyScalar(dist));
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
