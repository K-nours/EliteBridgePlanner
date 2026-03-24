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
  ChangeDetectorRef,
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
  conflicts: 0xcc5500,
  critical: 0xff6b6b,
  low: 0xe0e0e0,
  healthy: 0x00ff88,
  others: 0xaaaaaa,
};

const GLOW_CATEGORIES: Set<MapCategoryKey> = new Set(['origin', 'headquarter', 'critical']);
/** Repères galactiques (coordonnées ED en années-lumière). */
const GALACTIC_LANDMARKS = [
  { id: 'sol', name: 'Sol', x: 0, y: 0, z: 0, color: 0xffdd44, region: 'Bulle' },
  { id: 'ngc6357', name: 'NGC 6357 Sector KC-V c2-47', x: 946.81, y: 122.78, z: 8095.34, color: 0xffdd44, region: 'NGC 6357' },
  { id: 'sagA', name: 'Centre galactique', x: 25.22, y: -20.91, z: 25899.97, color: 0x9944ff },
  { id: 'colonia', name: 'Colonia', x: -9530.5, y: -910.28, z: 19808.13, color: 0x00cc99, region: 'Colonia' },
  { id: 'onoros', name: 'Onoros', x: 389.72, y: -379.94, z: -724.16, color: 0xddbb44, region: 'Witch Head' },
] as const;
/** Convention ED : X inversé pour que Colonia soit à gauche du centre galactique vu depuis Sol. */
const ED_TO_SCENE_X = (x: number) => -x;
/** Facteur de tolérance pour le raycast (hit area = rayon_visuel * HIT_TOLERANCE). */
const HIT_TOLERANCE = 2;
/** Facteur de scale au survol. */
const HOVER_SCALE = 1.3;

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
  private readonly cdr = inject(ChangeDetectorRef);

  private scene: THREE.Scene | null = null;
  private camera: THREE.PerspectiveCamera | null = null;
  private renderer: THREE.WebGLRenderer | null = null;
  private controls: OrbitControls | null = null;
  private raycaster = new THREE.Raycaster();
  private mouse = new THREE.Vector2();
  private meshMap = new Map<number, THREE.Mesh>();
  private hitMeshMap = new Map<number, THREE.Mesh>();
  private glowMeshes: THREE.Mesh[] = [];
  private animationId = 0;
  private hoveredSystem: MapSystem | null = null;

  /** Vue globale : barycentre + distance. Pour reset. */
  private viewCenter = new THREE.Vector3();
  private viewDistance = 300;

  tooltipData: { system: MapSystem; x: number; y: number } | null = null;
  selectedSystem: MapSystem | null = null;

  /** Zoom 0..100 : 0 = loin, 100 = proche. */
  zoomLevel = 50;

  /** Toggle repères galactiques (Sol, Centre galactique) + labels. */
  landmarksVisible = true;

  /** Positions 2D des labels pour le rendu (mis à jour dans animate). */
  landmarkLabels: { id: string; name: string; region?: string; x: number; y: number; visible: boolean }[] = [];

  private panInterval: ReturnType<typeof setInterval> | null = null;
  private landmarkMeshes: THREE.Mesh[] = [];
  private landmarkPositions: THREE.Vector3[] = [];

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
    this.addLandmarks();

    this.renderer.domElement.addEventListener('mousemove', (e) => this.onMouseMove(e));
    this.renderer.domElement.addEventListener('mouseleave', () => this.onMouseLeave());
    this.renderer.domElement.addEventListener('click', (e) => this.onClick(e));

    window.addEventListener('resize', () => this.onResize());
    this.updatePoints();
  }

  private addStarfield(): void {
    if (!this.scene) return;
    const count = 2200;
    const positions = new Float32Array(count * 3);
    const size = 12000;
    for (let i = 0; i < count * 3; i += 3) {
      positions[i] = (Math.random() - 0.5) * size;
      positions[i + 1] = (Math.random() - 0.5) * size;
      positions[i + 2] = (Math.random() - 0.5) * size;
    }
    const geo = new THREE.BufferGeometry().setAttribute('position', new THREE.BufferAttribute(positions, 3));
    const mat = new THREE.PointsMaterial({
      color: 0xffffff,
      size: 1.4,
      transparent: true,
      opacity: 0.6,
      sizeAttenuation: true,
    });
    const stars = new THREE.Points(geo, mat);
    this.scene.add(stars);
  }

  private addLandmarks(): void {
    if (!this.scene) return;
    for (const m of this.landmarkMeshes) this.scene.remove(m);
    this.landmarkMeshes = [];
    this.landmarkPositions = [];
    for (const lm of GALACTIC_LANDMARKS) {
      const pos = new THREE.Vector3(ED_TO_SCENE_X(lm.x), lm.y, lm.z);
      this.landmarkPositions.push(pos);
      const r = 6;
      const geom = new THREE.SphereGeometry(r, 12, 10);
      const mat = new THREE.MeshBasicMaterial({
        color: lm.color,
        transparent: true,
        opacity: 0.9,
      });
      const mesh = new THREE.Mesh(geom, mat);
      mesh.position.copy(pos);
      mesh.name = `landmark-${lm.id}`;
      mesh.renderOrder = 5;
      mesh.visible = this.landmarksVisible;
      this.scene.add(mesh);
      this.landmarkMeshes.push(mesh);
    }
  }

  private updateLandmarksVisibility(): void {
    for (const m of this.landmarkMeshes) {
      m.visible = this.landmarksVisible;
    }
  }

  private updatePoints(): void {
    if (!this.scene || !this.camera || !this.controls) return;

    this.clearHoverFocus();
    this.hoveredSystem = null;
    this.tooltipData = null;
    if (this.renderer?.domElement?.style) this.renderer.domElement.style.cursor = 'default';

    this.meshMap.forEach((m) => this.scene!.remove(m));
    this.meshMap.clear();
    this.hitMeshMap.forEach((m) => this.scene!.remove(m));
    this.hitMeshMap.clear();
    for (const glow of this.glowMeshes) this.scene!.remove(glow);
    this.glowMeshes = [];

    const list = this.systemsWithCoords;
    if (list.length === 0) return;

    let sumX = 0, sumY = 0, sumZ = 0;
    for (const sys of list) {
      sumX += ED_TO_SCENE_X(sys.coordsX!);
      sumY += sys.coordsY!;
      sumZ += sys.coordsZ!;
    }
    const n = list.length;
    const cx = sumX / n;
    const cy = sumY / n;
    const cz = sumZ / n;

    for (const sys of list) {
      const r = this.radiusFromInfluence(sys.influencePercent);
      const color = CATEGORY_COLORS[sys.mapCategory] ?? 0xffffff;

      const geometry = new THREE.SphereGeometry(r, 10, 8);
      const material = new THREE.MeshBasicMaterial({
        color,
        transparent: true,
        opacity: 0.85,
      });
      const mesh = new THREE.Mesh(geometry, material);
      mesh.position.set(ED_TO_SCENE_X(sys.coordsX!), sys.coordsY!, sys.coordsZ!);
      (mesh as THREE.Mesh & { systemData?: MapSystem }).systemData = sys;
      mesh.name = `sys-${sys.id}`;
      mesh.renderOrder = 0;
      this.scene.add(mesh);
      this.meshMap.set(sys.id, mesh);

      const hitRadius = r * HIT_TOLERANCE;
      const hitGeom = new THREE.SphereGeometry(hitRadius, 8, 6);
      const hitMat = new THREE.MeshBasicMaterial({ visible: false });
      const hitMesh = new THREE.Mesh(hitGeom, hitMat);
      hitMesh.position.copy(mesh.position);
      (hitMesh as THREE.Mesh & { systemData?: MapSystem }).systemData = sys;
      hitMesh.name = `hit-${sys.id}`;
      this.scene.add(hitMesh);
      this.hitMeshMap.set(sys.id, hitMesh);

      if (GLOW_CATEGORIES.has(sys.mapCategory)) {
        const glowGeom = new THREE.SphereGeometry(r * 1.15, 8, 6);
        const glowMat = new THREE.MeshBasicMaterial({
          color,
          transparent: true,
          opacity: 0.04,
        });
        const glow = new THREE.Mesh(glowGeom, glowMat);
        glow.position.copy(mesh.position);
        this.scene.add(glow);
        this.glowMeshes.push(glow);
      }
    }
    this.updateFilterHighlight();

    this.viewCenter.set(cx, cy, cz);
    this.controls.target.copy(this.viewCenter);
    let maxDist = 0;
    for (const sys of list) {
      const d = Math.hypot(ED_TO_SCENE_X(sys.coordsX!) - cx, sys.coordsY! - cy, sys.coordsZ! - cz);
      if (d > maxDist) maxDist = d;
    }
    this.viewDistance = Math.max(200, maxDist * 1.5);
    this.camera.position.set(cx + this.viewDistance, cy + this.viewDistance, cz + this.viewDistance);
  }

  private radiusFromInfluence(pct: number): number {
    const min = 0.8;
    const max = 4;
    return min + (pct / 100) * (max - min);
  }

  private updateFilterHighlight(): void {
    if (!this.scene) return;
    const filter = this.systemsFilter;
    const highlightCat = filter === 'all' ? null : (FILTER_TO_CATEGORY[filter] ?? null);
    for (const [id, mesh] of this.meshMap) {
      const mat = mesh.material as THREE.MeshBasicMaterial;
      const sys = (mesh as THREE.Mesh & { systemData?: MapSystem }).systemData;
      if (!sys) continue;
      if (this.hoveredSystem?.id === id) {
        mat.opacity = 1;
        continue;
      }
      const match = !highlightCat || sys.mapCategory === highlightCat;
      mat.opacity = match ? 0.85 : 0.2;
    }
  }

  private onMouseMove(event: MouseEvent): void {
    if (!this.renderer || !this.camera) return;
    const rect = this.renderer.domElement.getBoundingClientRect();
    this.mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    this.mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;

    this.raycaster.setFromCamera(this.mouse, this.camera);
    const hitMeshes = Array.from(this.hitMeshMap.values());
    const hits = this.raycaster.intersectObjects(hitMeshes);

    const hit = hits[0];
    const sys: MapSystem | null = hit?.object
      ? ((hit.object as THREE.Mesh & { systemData?: MapSystem }).systemData ?? null)
      : null;

    if (sys !== this.hoveredSystem) {
      this.clearHoverFocus();
      this.hoveredSystem = sys;
      if (sys) {
        this.applyHoverFocus(sys.id);
        this.tooltipData = { system: sys, x: event.clientX, y: event.clientY };
        this.renderer.domElement.style.cursor = 'pointer';
      } else {
        this.tooltipData = null;
        this.renderer.domElement.style.cursor = '';
      }
    } else if (sys) {
      this.tooltipData = { system: sys, x: event.clientX, y: event.clientY };
    }
  }

  private clearHoverFocus(): void {
    if (this.hoveredSystem) {
      const mesh = this.meshMap.get(this.hoveredSystem.id);
      if (mesh) {
        mesh.scale.setScalar(1);
        mesh.renderOrder = 0;
        const sys = (mesh as THREE.Mesh & { systemData?: MapSystem }).systemData;
        if (sys) {
          const mat = mesh.material as THREE.MeshBasicMaterial;
          mat.color.setHex(CATEGORY_COLORS[sys.mapCategory] ?? 0xffffff);
          mat.depthTest = true;
        }
      }
      this.updateFilterHighlight();
    }
  }

  private applyHoverFocus(systemId: number): void {
    const mesh = this.meshMap.get(systemId);
    if (!mesh) return;
    mesh.scale.setScalar(HOVER_SCALE);
    mesh.renderOrder = 10;
    const mat = mesh.material as THREE.MeshBasicMaterial;
    mat.opacity = 1;
    mat.color.setHex(0x00e5ff);
    mat.depthTest = false;
  }

  private onClick(_event: MouseEvent): void {
    if (!this.raycaster || !this.camera) return;
    this.raycaster.setFromCamera(this.mouse, this.camera);
    const hitMeshes = Array.from(this.hitMeshMap.values());
    const hits = this.raycaster.intersectObjects(hitMeshes);
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

  private onMouseLeave(): void {
    this.clearHoverFocus();
    this.hoveredSystem = null;
    this.tooltipData = null;
    if (this.renderer?.domElement?.style) this.renderer.domElement.style.cursor = 'default';
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
    const maxD = 1600;
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

  toggleLandmarks(): void {
    this.landmarksVisible = !this.landmarksVisible;
    this.updateLandmarksVisibility();
  }

  private animate = (): void => {
    this.animationId = requestAnimationFrame(this.animate);
    this.controls?.update();
    if (this.landmarksVisible && this.camera && this.renderer && this.landmarkPositions.length > 0) {
      const rect = this.renderer.domElement.getBoundingClientRect();
      const w = rect.width;
      const h = rect.height;
      const labels: { id: string; name: string; region?: string; x: number; y: number; visible: boolean }[] = [];
      const v = new THREE.Vector3();
      for (let i = 0; i < GALACTIC_LANDMARKS.length; i++) {
        v.copy(this.landmarkPositions[i]);
        v.project(this.camera!);
        const visible = v.z < 1 && v.x >= -1 && v.x <= 1 && v.y >= -1 && v.y <= 1;
        const x = ((v.x + 1) / 2) * w + rect.left;
        const y = ((1 - v.y) / 2) * h + rect.top;
        const lm = GALACTIC_LANDMARKS[i];
        labels.push({ id: lm.id, name: lm.name, region: 'region' in lm ? lm.region : undefined, x, y, visible });
      }
      this.landmarkLabels = labels;
      this.cdr.markForCheck();
    } else if (this.landmarkLabels.length > 0) {
      this.landmarkLabels = [];
      this.cdr.markForCheck();
    }
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
