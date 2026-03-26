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
import type { FrontierJournalSystemDerivedDto } from '../../../core/services/frontier-journal-api.service';

export type MapCategoryKey =
  | 'origin'
  | 'headquarter'
  | 'surveillance'
  | 'conflicts'
  | 'critical'
  | 'low'
  | 'healthy'
  | 'others';

/** Couleurs calques journal — néon / forte saturation pour rester visibles à grande distance. */
const JOURNAL_COLOR_VISITED = 0x00fff0;
const JOURNAL_COLOR_DISCOVERED = 0xff3df2;
const JOURNAL_COLOR_FULLSCAN = 0xfffc40;

/** Halo additif (MeshBasicMaterial.opacity) — Cmdr & Faction identiques. */
const HALO_OPACITY_INITIAL = 0.25;
const HALO_OPACITY_VISIBLE = 0.25;
const HALO_OPACITY_DIMMED = 0.05;
const HALO_OPACITY_HOVER = 0.5;

/** Low / others : même néon que « Visités » journal (distinct des pastels du panneau influence). */
const CATEGORY_COLORS: Record<MapCategoryKey, number> = {
  origin: 0x00d4ff,
  headquarter: 0xd4af37,
  surveillance: 0x93c5fd,
  conflicts: 0xcc5500,
  critical: 0xff6b6b,
  low: JOURNAL_COLOR_VISITED,
  healthy: 0x00ff88,
  others: JOURNAL_COLOR_VISITED,
};

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

/** Distance caméra ↔ cible : molette OrbitControls et curseur identiques. */
const MAP_ZOOM_DISTANCE_MIN = 5;
const MAP_ZOOM_DISTANCE_MAX = 40000;
/** Distance par défaut centre (HQ ou barycentre) → caméra : chargement + reset vue. */
const DEFAULT_MAP_VIEW_DISTANCE = 50;

function zoomLevelForViewDistance(dist: number): number {
  const minD = MAP_ZOOM_DISTANCE_MIN;
  const maxD = MAP_ZOOM_DISTANCE_MAX;
  const d = Math.max(minD, Math.min(maxD, dist));
  return Math.round(100 * (1 - (d - minD) / (maxD - minD)));
}

interface MapSystem extends GuildSystemBgsDto {
  mapCategory: MapCategoryKey;
  /** Point synthétique vue Cmdr (pas un système Inara / pas de clic panneau guilde). */
  isJournalCmdrPoint?: boolean;
}

/** Rayon du noyau (parsecs affichés) — plus large que l’ancien 1.15 pour la lisibilité. */
const CMDR_JOURNAL_CORE_RADIUS = 2.85;
/** Halo additif (vue Faction & Cmdr) : rayon = noyau × ce facteur. */
const MAP_SYSTEM_HALO_FACTOR = 1.35;
/** Noyau : légère transparence pour laisser un peu voir au travers (halo / profondeur). */
const MAP_SYSTEM_CORE_OPACITY = 0.92;
/** Couleur par défaut si pas de calque : glaçage clair (pas gris terne). */
const CMDR_JOURNAL_FALLBACK = 0x8effff;

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
  /** Vue Faction : systèmes guilde ; Vue Cmdr : points journal ; Pont galactique : ponts planifiés (placeholder). */
  @Input() mapViewMode: 'faction' | 'cmdr' | 'galacticBridge' = 'faction';
  /** Systèmes dérivés du journal ayant coordsX/Y/Z (passé par le parent). */
  @Input() journalCmdrPoints: FrontierJournalSystemDerivedDto[] = [];

  /** Calques journal CMDR (cumulables) — surbrillance sur la carte. */
  @Input() journalLayerVisited = false;
  @Input() journalLayerDiscovered = false;
  @Input() journalLayerFullScan = false;
  /** Clé = nom système normalisé (uppercase trim), valeurs depuis GET derived/systems. */
  @Input() journalByName: Record<string, { isVisited: boolean; hasFirstDiscoveryBody: boolean; isFullScanned: boolean }> = {};

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
  /** Halos additifs (même techno Cmdr / Faction). */
  private systemHaloById = new Map<number, THREE.Mesh>();
  private animationId = 0;
  private hoveredSystem: MapSystem | null = null;

  /** Vue globale : barycentre + distance. Pour reset. */
  private viewCenter = new THREE.Vector3();
  private viewDistance = 300;

  tooltipData: { system: MapSystem; x: number; y: number } | null = null;
  selectedSystem: MapSystem | null = null;

  /** Zoom 0..100 : 0 = loin, 100 = proche (aligné sur DEFAULT_MAP_VIEW_DISTANCE au départ). */
  zoomLevel = zoomLevelForViewDistance(DEFAULT_MAP_VIEW_DISTANCE);

  /** Toggle repères galactiques (Sol, Centre galactique) + labels. */
  landmarksVisible = true;

  /** Positions 2D des labels pour le rendu (mis à jour dans animate). */
  landmarkLabels: { id: string; name: string; region?: string; x: number; y: number; visible: boolean }[] = [];

  private panInterval: ReturnType<typeof setInterval> | null = null;
  private zoomInterval: ReturnType<typeof setInterval> | null = null;
  private landmarkMeshes: THREE.Mesh[] = [];
  private landmarkPositions: THREE.Vector3[] = [];

  constructor() {
    effect(() => {
      const id = this.guildSync.focusOnSystemId();
      if (id != null && id > 0 && this.camera && this.controls) this.focusOnSystemId(id);
    });
  }

  /**
   * Vue Faction : pas de points si aucun système guilde n’a de coords EDSM.
   * Vue Cmdr : pas de points si le journal parsé n’a aucune coordonnée StarPos.
   */
  hasNoCoords(): boolean {
    return this.buildMapPointsList().length === 0;
  }

  /** Points affichés sur la carte selon la vue (sources strictement séparées). */
  private buildMapPointsList(): MapSystem[] {
    if (this.mapViewMode === 'galacticBridge') {
      return [];
    }
    if (this.mapViewMode === 'faction') {
      return guildInputToFactionMapSystems(this.systems);
    }
    const withCoords = this.journalCmdrPoints.filter(
      (p) => p.coordsX != null && p.coordsY != null && p.coordsZ != null,
    );
    if (withCoords.length === 0) return [];
    return withCoords.map((p) => journalDtoToMapSystemMerged(p, this.systems));
  }

  ngOnInit(): void {}

  ngAfterViewInit(): void {
    this.initScene();
    this.animate();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const systemsCh = !!changes['systems'];
    const modeCh = !!changes['mapViewMode'];
    const journalCh = !!changes['journalCmdrPoints'];
    /** Changement Faction / Cmdr / Pont uniquement : ne pas recadrer la caméra. */
    const preserveCamera = modeCh && !systemsCh && !journalCh;

    if (systemsCh || modeCh || journalCh) {
      if (this.scene) this.updatePoints(preserveCamera);
    } else if (
      (changes['systemsFilter'] ||
        changes['journalLayerVisited'] ||
        changes['journalLayerDiscovered'] ||
        changes['journalLayerFullScan'] ||
        changes['journalByName']) &&
      this.scene
    ) {
      this.updateVisualHighlight();
    }
  }

  ngOnDestroy(): void {
    this.stopPan();
    this.stopZoomHold();
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
    this.controls.zoomToCursor = true;
    this.controls.minDistance = MAP_ZOOM_DISTANCE_MIN;
    this.controls.maxDistance = MAP_ZOOM_DISTANCE_MAX;

    this.addStarfield();
    this.addLandmarks();

    this.renderer.domElement.addEventListener('mousemove', (e) => this.onMouseMove(e));
    this.renderer.domElement.addEventListener('mouseleave', () => this.onMouseLeave());
    this.renderer.domElement.addEventListener('click', (e) => this.onClick(e));

    window.addEventListener('resize', () => this.onResize());
    this.updatePoints(false);
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

  private updatePoints(preserveCamera = false): void {
    if (!this.scene || !this.camera || !this.controls) return;

    this.clearHoverFocus();
    this.hoveredSystem = null;
    this.tooltipData = null;
    if (this.renderer?.domElement?.style) this.renderer.domElement.style.cursor = 'default';

    this.meshMap.forEach((m) => this.scene!.remove(m));
    this.meshMap.clear();
    this.hitMeshMap.forEach((m) => this.scene!.remove(m));
    this.hitMeshMap.clear();
    for (const glow of this.systemHaloById.values()) this.scene!.remove(glow);
    this.systemHaloById.clear();

    const list = this.buildMapPointsList();
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
      const r = sys.isJournalCmdrPoint ? CMDR_JOURNAL_CORE_RADIUS : this.radiusFromInfluence(sys.influencePercent);
      const color = sys.isJournalCmdrPoint ? CMDR_JOURNAL_FALLBACK : CATEGORY_COLORS[sys.mapCategory] ?? JOURNAL_COLOR_VISITED;

      const geometry = new THREE.SphereGeometry(r, 10, 8);
      const material = new THREE.MeshBasicMaterial({
        color,
        transparent: true,
        opacity: MAP_SYSTEM_CORE_OPACITY,
        depthTest: true,
      });
      const mesh = new THREE.Mesh(geometry, material);
      mesh.position.set(ED_TO_SCENE_X(sys.coordsX!), sys.coordsY!, sys.coordsZ!);
      (mesh as THREE.Mesh & { systemData?: MapSystem }).systemData = sys;
      mesh.name = `sys-${sys.id}`;
      mesh.renderOrder = 2;
      this.scene.add(mesh);
      this.meshMap.set(sys.id, mesh);

      const glowR = r * MAP_SYSTEM_HALO_FACTOR;
      const glowGeom = new THREE.SphereGeometry(glowR, 10, 8);
      const glowMat = new THREE.MeshBasicMaterial({
        color,
        transparent: true,
        opacity: HALO_OPACITY_INITIAL,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
      });
      const glowMesh = new THREE.Mesh(glowGeom, glowMat);
      glowMesh.position.copy(mesh.position);
      glowMesh.renderOrder = 1;
      this.scene.add(glowMesh);
      this.systemHaloById.set(sys.id, glowMesh);

      const hitRadius = r * HIT_TOLERANCE;
      const hitGeom = new THREE.SphereGeometry(hitRadius, 8, 6);
      const hitMat = new THREE.MeshBasicMaterial({ visible: false });
      const hitMesh = new THREE.Mesh(hitGeom, hitMat);
      hitMesh.position.copy(mesh.position);
      (hitMesh as THREE.Mesh & { systemData?: MapSystem }).systemData = sys;
      hitMesh.name = `hit-${sys.id}`;
      this.scene.add(hitMesh);
      this.hitMeshMap.set(sys.id, hitMesh);
    }
    this.updateVisualHighlight();

    const defaultDist = DEFAULT_MAP_VIEW_DISTANCE;
    const defaultViewDir = new THREE.Vector3(1, 1, 1).normalize();

    const hqSys = list.find(
      (s) => s.mapCategory === 'headquarter' && s.coordsX != null && s.coordsY != null && s.coordsZ != null,
    );

    if (hqSys) {
      this.viewCenter.set(ED_TO_SCENE_X(hqSys.coordsX!), hqSys.coordsY!, hqSys.coordsZ!);
    } else {
      this.viewCenter.set(cx, cy, cz);
    }

    if (!preserveCamera) {
      this.controls.target.copy(this.viewCenter);
      this.zoomLevel = zoomLevelForViewDistance(defaultDist);
      this.viewDistance = defaultDist;
      this.camera.position.copy(this.viewCenter).add(defaultViewDir.clone().multiplyScalar(defaultDist));
    } else {
      const dist = this.camera.position.distanceTo(this.controls.target);
      this.viewDistance = dist;
      this.zoomLevel = zoomLevelForViewDistance(dist);
    }
  }

  /** Noyau : rayon minR → maxR selon influence 1–80 % (écart large pour lisibilité). */
  private radiusFromInfluence(pct: number): number {
    const minR = 0.45;
    const maxR = 7;
    const pctMin = 1;
    const pctMax = 80;
    const clamped = Math.max(pctMin, Math.min(pctMax, pct));
    const t = (clamped - pctMin) / (pctMax - pctMin);
    return minR + t * (maxR - minR);
  }

  private journalLayersActive(): boolean {
    return this.journalLayerVisited || this.journalLayerDiscovered || this.journalLayerFullScan;
  }

  private normalizeJournalKey(name: string): string {
    return name.trim().toUpperCase();
  }

  /** Priorité affichage : full scan > première découverte (corps) > visité. */
  private journalAccentColor(sys: MapSystem): number | null {
    if (!this.journalLayersActive()) return null;
    const j = this.journalByName[this.normalizeJournalKey(sys.name)];
    if (!j) return null;
    if (this.journalLayerFullScan && j.isFullScanned) return JOURNAL_COLOR_FULLSCAN;
    if (this.journalLayerDiscovered && j.hasFirstDiscoveryBody) return JOURNAL_COLOR_DISCOVERED;
    if (this.journalLayerVisited && j.isVisited) return JOURNAL_COLOR_VISITED;
    return null;
  }

  private journalMatchesActiveLayers(sys: MapSystem): boolean {
    const j = this.journalByName[this.normalizeJournalKey(sys.name)];
    if (!j) return false;
    return (
      (this.journalLayerVisited && j.isVisited) ||
      (this.journalLayerDiscovered && j.hasFirstDiscoveryBody) ||
      (this.journalLayerFullScan && j.isFullScanned)
    );
  }

  private journalCmdrDefaultColor(sys: MapSystem): number {
    const j = this.journalByName[this.normalizeJournalKey(sys.name)];
    if (!j) return CMDR_JOURNAL_FALLBACK;
    if (j.isFullScanned) return JOURNAL_COLOR_FULLSCAN;
    if (j.hasFirstDiscoveryBody) return JOURNAL_COLOR_DISCOVERED;
    if (j.isVisited) return JOURNAL_COLOR_VISITED;
    return CMDR_JOURNAL_FALLBACK;
  }

  private updateVisualHighlight(): void {
    if (!this.scene) return;
    const filter = this.systemsFilter;
    const highlightCat = filter === 'all' ? null : (FILTER_TO_CATEGORY[filter] ?? null);
    const journalOn = this.journalLayersActive();
    const cmdr = this.mapViewMode === 'cmdr';

    for (const [id, mesh] of this.meshMap) {
      const mat = mesh.material as THREE.MeshBasicMaterial;
      const sys = (mesh as THREE.Mesh & { systemData?: MapSystem }).systemData;
      if (!sys) continue;
      if (this.hoveredSystem?.id === id) {
        mat.opacity = 1;
        continue;
      }
      if (cmdr) {
        const matchJ = !journalOn || this.journalMatchesActiveLayers(sys);
        mat.opacity = matchJ ? MAP_SYSTEM_CORE_OPACITY : 0.18;
        const accent = this.journalAccentColor(sys);
        let coreHex: number;
        if (journalOn && accent != null && matchJ) {
          coreHex = accent;
          mat.color.setHex(accent);
        } else if (!journalOn) {
          coreHex = this.journalCmdrDefaultColor(sys);
          mat.color.setHex(coreHex);
        } else {
          coreHex = CATEGORY_COLORS[sys.mapCategory] ?? JOURNAL_COLOR_VISITED;
          mat.color.setHex(coreHex);
        }
        const halo = this.systemHaloById.get(id);
        if (halo) {
          const hm = halo.material as THREE.MeshBasicMaterial;
          hm.color.setHex(coreHex);
          hm.opacity = matchJ ? HALO_OPACITY_VISIBLE : HALO_OPACITY_DIMMED;
        }
        continue;
      }
      const matchCat = !highlightCat || sys.mapCategory === highlightCat;
      const matchJ = !journalOn || this.journalMatchesActiveLayers(sys);
      const visible = matchCat && matchJ;
      mat.opacity = visible ? MAP_SYSTEM_CORE_OPACITY : 0.2;

      const accent = this.journalAccentColor(sys);
      let coreHex: number;
      if (journalOn && accent != null && matchJ && matchCat) {
        coreHex = accent;
        mat.color.setHex(accent);
      } else {
        coreHex = CATEGORY_COLORS[sys.mapCategory] ?? JOURNAL_COLOR_VISITED;
        mat.color.setHex(coreHex);
      }
      const halo = this.systemHaloById.get(id);
      if (halo) {
        const hm = halo.material as THREE.MeshBasicMaterial;
        hm.color.setHex(coreHex);
        hm.opacity = visible ? HALO_OPACITY_VISIBLE : HALO_OPACITY_DIMMED;
      }
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
        mesh.renderOrder = 2;
        const sys = (mesh as THREE.Mesh & { systemData?: MapSystem }).systemData;
        if (sys) {
          const mat = mesh.material as THREE.MeshBasicMaterial;
          mat.color.setHex(CATEGORY_COLORS[sys.mapCategory] ?? JOURNAL_COLOR_VISITED);
          mat.depthTest = true;
        }
      }
      this.updateVisualHighlight();
    }
  }

  private applyHoverFocus(systemId: number): void {
    const mesh = this.meshMap.get(systemId);
    if (!mesh) return;
    mesh.scale.setScalar(HOVER_SCALE);
    mesh.renderOrder = 10;
    const mat = mesh.material as THREE.MeshBasicMaterial;
    const sys = (mesh as THREE.Mesh & { systemData?: MapSystem }).systemData;
    mat.opacity = 1;
    mat.color.setHex(sys?.isJournalCmdrPoint ? 0xffffff : 0x00e5ff);
    mat.depthTest = false;
    const g = this.systemHaloById.get(systemId);
    if (g) {
      const gm = g.material as THREE.MeshBasicMaterial;
      gm.color.setHex(0xffffff);
      gm.opacity = HALO_OPACITY_HOVER;
    }
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
      if (sys.isJournalCmdrPoint) {
        this.guildSync.clearMapSelection();
      } else {
        this.guildSync.onMapSystemClicked(sys.name, sys.id);
      }
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
    const dist = DEFAULT_MAP_VIEW_DISTANCE;
    this.zoomLevel = zoomLevelForViewDistance(dist);
    this.controls.target.copy(this.viewCenter);
    this.viewDistance = dist;
    const dir = new THREE.Vector3(1, 1, 1).normalize();
    this.camera.position.copy(this.viewCenter).add(dir.multiplyScalar(dist));
  }

  panCamera(dx: number, dy: number, dz: number): void {
    if (!this.camera || !this.controls) return;
    const d = 50;
    this.controls.target.add(new THREE.Vector3(dx * d, dy * d, dz * d));
    this.camera.position.add(new THREE.Vector3(dx * d, dy * d, dz * d));
  }

  startPan(ev: PointerEvent, dx: number, dy: number, dz: number): void {
    this.stopPan();
    this.stopZoomHold();
    (ev.currentTarget as HTMLElement)?.setPointerCapture(ev.pointerId);
    this.panInterval = setInterval(() => this.panCamera(dx, dy, dz), 50);
  }

  stopPan(): void {
    if (this.panInterval) {
      clearInterval(this.panInterval);
      this.panInterval = null;
    }
  }

  startZoomHold(ev: PointerEvent, delta: number): void {
    this.stopZoomHold();
    this.stopPan();
    (ev.currentTarget as HTMLElement)?.setPointerCapture(ev.pointerId);
    this.zoomStep(delta);
    this.zoomInterval = setInterval(() => this.zoomStep(delta), 50);
  }

  stopZoomHold(): void {
    if (this.zoomInterval) {
      clearInterval(this.zoomInterval);
      this.zoomInterval = null;
    }
  }

  zoomStep(delta: number): void {
    this.setZoomFromLevel(this.zoomLevel + delta);
  }

  /**
   * Zoom UI (+/−, curseur) : distance par rapport au centre de la carte (barycentre des points),
   * pas le pivot Orbit (qui suit le pan). La molette garde zoomToCursor sur le pointeur.
   */
  setZoomFromLevel(level: number): void {
    this.zoomLevel = Math.round(Math.max(0, Math.min(100, level)));
    if (!this.camera || !this.controls) return;
    const anchor = this.viewCenter;
    const minD = MAP_ZOOM_DISTANCE_MIN;
    const maxD = MAP_ZOOM_DISTANCE_MAX;
    const dist = minD + (1 - this.zoomLevel / 100) * (maxD - minD);

    let dir = new THREE.Vector3().subVectors(this.camera.position, anchor);
    if (dir.lengthSq() < 1e-10) {
      dir.set(1, 1, 1);
    }
    dir.normalize();

    this.controls.target.copy(anchor);
    this.camera.position.copy(anchor).add(dir.multiplyScalar(dist));
  }

  /** Inverse de la formule du slider : distance orbit ↔ niveau 0 (loin) … 100 (proche). */
  private distanceToZoomLevel(dist: number): number {
    const minD = MAP_ZOOM_DISTANCE_MIN;
    const maxD = MAP_ZOOM_DISTANCE_MAX;
    const d = Math.max(minD, Math.min(maxD, dist));
    return 100 * (1 - (d - minD) / (maxD - minD));
  }

  /** Garde le curseur aligné sur la distance réelle après molette / pinch OrbitControls. */
  private syncZoomSliderFromOrbit(): void {
    if (!this.camera || !this.controls) return;
    const dist = this.camera.position.distanceTo(this.controls.target);
    const level = this.distanceToZoomLevel(dist);
    const rounded = Math.round(level);
    if (rounded === this.zoomLevel) return;
    this.zoomLevel = rounded;
    this.viewDistance = dist;
    this.cdr.markForCheck();
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
    this.syncZoomSliderFromOrbit();
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

function syntheticJournalId(systemName: string): number {
  let h = 0;
  const s = systemName.trim().toUpperCase();
  for (let i = 0; i < s.length; i++) {
    h = (h << 5) - h + s.charCodeAt(i);
    h |= 0;
  }
  return h <= 0 ? h - 1 : -h;
}

const GUILD_CATEGORY_SCAN_ORDER: { cat: MapCategoryKey; key: keyof GuildSystemsResponseInput }[] = [
  { cat: 'origin', key: 'origin' },
  { cat: 'headquarter', key: 'headquarter' },
  { cat: 'surveillance', key: 'surveillance' },
  { cat: 'conflicts', key: 'conflicts' },
  { cat: 'critical', key: 'critical' },
  { cat: 'low', key: 'low' },
  { cat: 'healthy', key: 'healthy' },
  { cat: 'others', key: 'others' },
];

function findGuildMatchForJournalName(
  name: string,
  systems: GuildSystemsResponseInput,
): { sys: GuildSystemBgsDto; cat: MapCategoryKey } | null {
  const key = name.trim().toUpperCase();
  for (const { cat, key: arrKey } of GUILD_CATEGORY_SCAN_ORDER) {
    for (const s of systems[arrKey] ?? []) {
      if (s.name.trim().toUpperCase() === key) return { sys: s, cat };
    }
  }
  return null;
}

/** Positions = journal CAPI (StarPos) ; métadonnées BGS = guilde si le nom correspond. */
function journalDtoToMapSystemMerged(
  dto: FrontierJournalSystemDerivedDto,
  systems: GuildSystemsResponseInput,
): MapSystem {
  const m = findGuildMatchForJournalName(dto.systemName, systems);
  if (!m) {
    return {
      id: syntheticJournalId(dto.systemName),
      name: dto.systemName,
      influencePercent: 0,
      isThreatened: false,
      isExpansionCandidate: false,
      isHeadquarter: false,
      isUnderSurveillance: false,
      isClean: true,
      category: 'Journal CAPI',
      isFromSeed: false,
      coordsX: dto.coordsX!,
      coordsY: dto.coordsY!,
      coordsZ: dto.coordsZ!,
      mapCategory: 'others',
      isJournalCmdrPoint: true,
    };
  }
  const { sys, cat } = m;
  return {
    ...sys,
    coordsX: dto.coordsX!,
    coordsY: dto.coordsY!,
    coordsZ: dto.coordsZ!,
    mapCategory: cat,
    isJournalCmdrPoint: false,
  };
}

/** Vue Faction uniquement : systèmes guilde enrichis (coords EDSM), sans passer par le journal CMDR. */
function guildInputToFactionMapSystems(input: GuildSystemsResponseInput): MapSystem[] {
  const list: MapSystem[] = [];
  for (const { cat, key: arrKey } of GUILD_CATEGORY_SCAN_ORDER) {
    for (const s of input[arrKey] ?? []) {
      if (s.coordsX == null || s.coordsY == null || s.coordsZ == null) continue;
      list.push({
        ...s,
        coordsX: s.coordsX,
        coordsY: s.coordsY,
        coordsZ: s.coordsZ,
        mapCategory: cat,
        isJournalCmdrPoint: false,
      });
    }
  }
  return list;
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
