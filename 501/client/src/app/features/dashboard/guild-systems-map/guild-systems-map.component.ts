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
import { hasConflictState } from '../../../core/utils/guild-systems.util';
import { isInaraWithoutNewsCategory } from '../../../core/utils/inara-data-derivation.util';

export type MapCategoryKey =
  | 'origin'
  | 'headquarter'
  | 'surveillance'
  | 'conflicts'
  | 'critical'
  | 'low'
  | 'healthy'
  | 'others';

/** Low / others : couleur néon cyan (distinct des pastels du panneau influence). */
const CATEGORY_NEON_CYAN = 0x00fff0;
const CATEGORY_COLORS: Record<MapCategoryKey, number> = {
  origin: 0x00d4ff,
  headquarter: 0xd4af37,
  surveillance: 0x93c5fd,
  conflicts: 0xcc5500,
  critical: 0xff6b6b,
  low: CATEGORY_NEON_CYAN,
  healthy: 0x00ff88,
  others: CATEGORY_NEON_CYAN,
};

/**
 * Repères galactiques (coordonnées ED en années-lumière).
 * Couleurs stellaires : même logique que les systèmes (`inferStarPalette`), lettres spectrales
 * alignées sur EDSM / fiche système ; **Sol = G, Colonia = F** vérifiés sur Inara.
 */
const GALACTIC_LANDMARKS = [
  { id: 'sol', name: 'Sol', x: 0, y: 0, z: 0, region: 'Bulle', primaryStarClass: 'G' }, // classe G (Inara)
  { id: 'sagA', name: 'Centre galactique', x: 25.22, y: -20.91, z: 25899.97, color: 0x9944ff },
  { id: 'colonia', name: 'Colonia', x: -9530.5, y: -910.28, z: 19808.13, region: 'Colonia', primaryStarClass: 'F' }, // classe F (Inara)
  { id: 'onoros', name: 'Onoros', x: 389.72, y: -379.94, z: -724.16, region: 'Witch Head', primaryStarClass: 'K' }, // K (Yellow-Orange) Star
  /** Coords EDSM (coordsLocked) ; K (Yellow-Orange) Star */
  {
    id: 'beaglePoint',
    name: 'Beagle Point',
    x: -1111.5625,
    y: -134.21875,
    z: 65269.75,
    region: 'Bordure galactique',
    primaryStarClass: 'K',
  },
] as const;

/** Distance euclidienne au Sol (0, 0, 0) en coordonnées ED, en années-lumière. */
function landmarkDistanceFromSolLy(lm: { x: number; y: number; z: number }): number {
  return Math.hypot(lm.x, lm.y, lm.z);
}

/** Convention ED : X inversé pour que Colonia soit à gauche du centre galactique vu depuis Sol. */
const ED_TO_SCENE_X = (x: number) => -x;
/** Facteur de scale au survol. */
const HOVER_SCALE = 1.3;

/** Décalage vertical (px) des libellés repères : sous le point projeté pour ne pas masquer l'étoile / le système. */
const LANDMARK_LABEL_OFFSET_Y_PX = 40;

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
}

/** Repère galactique rendu comme un système carte (billboards + glow), id négatif dédié. */
function createSyntheticLandmarkMapSystem(lm: (typeof GALACTIC_LANDMARKS)[number], index: number): MapSystem {
  return {
    id: -1000 - index,
    name: lm.name,
    influencePercent: 0,
    isThreatened: false,
    isExpansionCandidate: false,
    isHeadquarter: false,
    isUnderSurveillance: false,
    isClean: true,
    category: 'Repère',
    isFromSeed: false,
    coordsX: lm.x,
    coordsY: lm.y,
    coordsZ: lm.z,
    primaryStarClass: 'primaryStarClass' in lm ? lm.primaryStarClass : null,
    mapCategory: 'others',
  };
}

/**
 * Rayon de base carte (anneaux + noyau) quand la faction n'a pas d'influence dans le système :
 * pas de disque d'influence BGS, seulement l'étoile + marqueurs catégorie (HQ, etc.).
 */
const STAR_MAP_NO_FACTION_BASE_R = 0.5;

/** Disque d'influence : affiché seulement si la faction a une influence &gt; 0 (présence Inara / sync). */
function hasFactionInfluenceOnMap(sys: Pick<GuildSystemBgsDto, 'influencePercent'>): boolean {
  const p = sys.influencePercent;
  return typeof p === 'number' && !Number.isNaN(p) && p > 0;
}

/** Multiplicateur global sur l'alpha des shaders radiaux (étoile / influence). */
const STAR_ALPHA_SCALE = 1.0;
const INFLUENCE_DISK_OPACITY = 0.22;
/** Au survol : opacité du grand disque d'influence (uniforme `uAlphaScale` du shader). */
const INFLUENCE_DISK_HOVER_OPACITY = 0.8;
const RING_PRIMARY_OPACITY = 0.88;
/** Anneau catégorie extérieur — plus discret (demi-largeur radiale vs ancienne version). */
const RING_SECONDARY_OPACITY = 0.14;
const DIM_FACTOR_CORE = 0.28;
const DIM_FACTOR_INFLUENCE = 0.12;
const DIM_FACTOR_RING1 = 0.35;
const DIM_FACTOR_RING2 = 0.08;

/** Épaisseur relative de la bordure filtre (≈ lisibilité « 2px » à l'échelle du disque). */
const RING_PRIMARY_BORDER_FRAC = 0.022;

/**
 * Rayon extérieur du disque visuel (anneau secondaire / halo catégorie), aligné sur `buildSystemVisualGroup`.
 * Sert au rayon de la sphère de picking : moitié de ce rayon pour limiter les chevauchements entre systèmes voisins.
 */
function visualSystemDiskOuterRadius(baseR: number): number {
  const borderW = Math.max(0.04, baseR * RING_PRIMARY_BORDER_FRAC);
  const ring1Outer = baseR + borderW;
  const r2Inner = ring1Outer + borderW * 0.25;
  const r2OuterFull = baseR * 1.28;
  return r2Inner + (r2OuterFull - r2Inner) * 0.5;
}

interface SystemVisualLayers {
  /** Billboards : ShaderMaterial radial (étoile + disque influence). */
  starCore: THREE.Mesh;
  influenceDisk: THREE.Mesh;
  ringPrimary: THREE.Mesh;
  ringSecondary: THREE.Mesh;
}

const RADIAL_BILLBOARD_VERT = `
varying vec2 vUv;
void main() {
  vUv = uv;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}
`;

/** Centre quasi blanc / saturé, dissipation progressive — pas de bord net. */
const STAR_RADIAL_FRAG = `
varying vec2 vUv;
uniform vec3 uCoreColor;
uniform float uAlphaScale;

void main() {
  vec2 p = vUv - 0.5;
  float d = length(p) * 2.0;
  vec3 col = mix(vec3(1.0), uCoreColor, smoothstep(0.0, 0.38, d));
  float a = 1.0 - smoothstep(0.1, 0.995, d);
  a *= smoothstep(1.0, 0.9, d);
  a = pow(max(a, 0.0), 0.88);
  gl_FragColor = vec4(col, a * uAlphaScale);
}
`;

/** Influence : alpha fort au centre, fade jusqu'à quasi 0 sur le bord extérieur. */
const INFLUENCE_RADIAL_FRAG = `
varying vec2 vUv;
uniform vec3 uTint;
uniform float uAlphaScale;

void main() {
  vec2 p = vUv - 0.5;
  float d = length(p) * 2.0;
  float a = (1.0 - smoothstep(0.0, 0.99, d)) * uAlphaScale;
  a *= 1.0 - smoothstep(0.68, 1.0, d);
  gl_FragColor = vec4(uTint, a);
}
`;

function createStarRadialMaterial(coreHex: number, alphaScale: number): THREE.ShaderMaterial {
  return new THREE.ShaderMaterial({
    uniforms: {
      uCoreColor: { value: new THREE.Color(coreHex) },
      uAlphaScale: { value: alphaScale },
    },
    vertexShader: RADIAL_BILLBOARD_VERT,
    fragmentShader: STAR_RADIAL_FRAG,
    transparent: true,
    depthWrite: false,
    depthTest: true,
    side: THREE.DoubleSide,
    blending: THREE.AdditiveBlending,
  });
}

function createInfluenceRadialMaterial(tintHex: number, alphaScale: number): THREE.ShaderMaterial {
  return new THREE.ShaderMaterial({
    uniforms: {
      uTint: { value: new THREE.Color(tintHex) },
      uAlphaScale: { value: alphaScale },
    },
    vertexShader: RADIAL_BILLBOARD_VERT,
    fragmentShader: INFLUENCE_RADIAL_FRAG,
    transparent: true,
    depthWrite: false,
    depthTest: true,
    side: THREE.DoubleSide,
    blending: THREE.NormalBlending,
  });
}

/**
 * Lettre spectrale quand l'API n'a pas encore `primaryStarClass` (fallback carte).
 * Référence joueur : **Sol = G, Colonia = F** (vérifié Inara) ; le reste inchangé.
 */
const KNOWN_SPECTRAL_LETTER_BY_SYSTEM_NAME: Record<string, string> = {
  SOL: 'G',
  COLONIA: 'F',
  ONOROS: 'K',
  'BEAGLE POINT': 'K',
};

/** Noyau stellaire + teinte du disque d'influence (plus pâle / désaturée). */
function inferStarPalette(
  primaryStarClass: string | null | undefined,
  systemName: string,
): { core: number; influence: number } {
  const nameKey = systemName.trim().toUpperCase();
  const fromApi = (primaryStarClass ?? '').trim();
  const fromKnown = KNOWN_SPECTRAL_LETTER_BY_SYSTEM_NAME[nameKey];
  const raw = (fromApi || fromKnown || '').toUpperCase();
  const first = raw.charAt(0);
  // O, B, A → bleu ; K, M → rouge ; F / G distincts ; naines blanches / neutrons bleutées
  if (first === 'O' || first === 'B' || first === 'A' || /WOLF|NEUTRON|WHITE DWARF|WD/i.test(raw)) {
    return { core: 0x7ec8ff, influence: 0x4a88cc };
  }
  if (first === 'K' || first === 'M' || /RED|CARBON|BROWN/i.test(raw)) {
    return { core: 0xff7744, influence: 0xcc5030 };
  }
  if (first === 'F') {
    return { core: 0xfff0dd, influence: 0xc9b898 };
  }
  if (first === 'G' || /YELLOW/i.test(raw)) {
    return { core: 0xffcc55, influence: 0xd4a82a };
  }
  // Fallback déterministe par nom (bleu / rouge / jaune) pour variété sans données spectrales
  let h = 0;
  const s = nameKey;
  for (let i = 0; i < s.length; i++) h = (h << 5) - h + s.charCodeAt(i);
  const t = Math.abs(h) % 3;
  if (t === 0) return { core: 0x88ccff, influence: 0x5599dd };
  if (t === 1) return { core: 0xff9966, influence: 0xcc6644 };
  return { core: 0xffee99, influence: 0xc9b85c };
}

/** Survol : éclaircit le noyau spectral au lieu d'un cyan fixe (qui masquait Sol, etc.). */
function hoverStarCoreHex(spectralCoreHex: number): number {
  const c = new THREE.Color(spectralCoreHex);
  c.lerp(new THREE.Color(0xffffff), 0.4);
  return c.getHex();
}

/** Assombrit légèrement une couleur néon pour le disque d'influence (lisibilité, pas un pâté opaque). */
function tintForInfluenceDisk(hex: number): number {
  const c = new THREE.Color(hex);
  c.lerp(new THREE.Color(0x0a1620), 0.42);
  return c.getHex();
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
  /** Vue Faction : systèmes guilde ; Pont galactique : ponts planifiés (placeholder). */
  @Input() mapViewMode: 'faction' | 'galacticBridge' = 'faction';

  private readonly guildSync = inject(GuildSystemsSyncService);
  private readonly cdr = inject(ChangeDetectorRef);

  private scene: THREE.Scene | null = null;
  private camera: THREE.PerspectiveCamera | null = null;
  private renderer: THREE.WebGLRenderer | null = null;
  private controls: OrbitControls | null = null;
  private raycaster = new THREE.Raycaster();
  private mouse = new THREE.Vector2();
  /** Groupe racine par système (étoile + disque + anneaux) — position / scale hover / focus. */
  private systemGroups = new Map<number, THREE.Group>();
  private systemLayers = new Map<number, SystemVisualLayers>();
  private hitMeshMap = new Map<number, THREE.Mesh>();
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
  landmarkLabels: {
    id: string;
    name: string;
    region?: string;
    distanceFromSolLy: number;
    x: number;
    y: number;
    visible: boolean;
  }[] = [];

  private panInterval: ReturnType<typeof setInterval> | null = null;
  private zoomInterval: ReturnType<typeof setInterval> | null = null;
  /** Repères : même pipeline 3D que les systèmes (glow radial), pas des sphères unies. */
  private landmarkGroups: THREE.Group[] = [];
  private landmarkHitMeshes: THREE.Mesh[] = [];
  private landmarkLayers: SystemVisualLayers[] = [];
  private landmarkPositions: THREE.Vector3[] = [];

  constructor() {
    effect(() => {
      const id = this.guildSync.focusOnSystemId();
      if (id != null && id > 0 && this.camera && this.controls) this.focusOnSystemId(id);
    });
  }

  /**
   * Vue Faction : pas de points si aucun système guilde n'a de coords EDSM.
   * Vue Cmdr : pas de points si le journal parsé n'a aucune coordonnée StarPos.
   */
  hasNoCoords(): boolean {
    return this.buildMapPointsList().length === 0;
  }

  /** Points affichés sur la carte selon la vue (sources strictement séparées). */
  private buildMapPointsList(): MapSystem[] {
    if (this.mapViewMode === 'galacticBridge') {
      return [];
    }
    return guildInputToFactionMapSystems(this.systems);
  }

  ngOnInit(): void {}

  ngAfterViewInit(): void {
    this.initScene();
    this.animate();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const systemsCh = !!changes['systems'];
    const modeCh = !!changes['mapViewMode'];
    /** Changement Faction / Pont uniquement : ne pas recadrer la caméra. */
    const preserveCamera = modeCh && !systemsCh;

    if (systemsCh || modeCh) {
      if (this.scene) this.updatePoints(preserveCamera);
    } else if (changes['systemsFilter'] && this.scene) {
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
    this.renderer.domElement.addEventListener('dblclick', (e) => this.onDblClick(e));

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
    for (const g of this.landmarkGroups) this.scene.remove(g);
    for (const h of this.landmarkHitMeshes) this.scene.remove(h);
    this.landmarkGroups = [];
    this.landmarkHitMeshes = [];
    this.landmarkLayers = [];
    this.landmarkPositions = [];

    GALACTIC_LANDMARKS.forEach((lm, i) => {
      const pos = new THREE.Vector3(ED_TO_SCENE_X(lm.x), lm.y, lm.z);
      this.landmarkPositions.push(pos);
      const sys = createSyntheticLandmarkMapSystem(lm, i);
      const paletteOpts =
        'color' in lm && lm.color !== undefined
          ? { palette: { core: lm.color, influence: tintForInfluenceDisk(lm.color) } }
          : undefined;
      const { group, layers, hitMesh } = this.buildSystemVisualGroup(
        sys,
        STAR_MAP_NO_FACTION_BASE_R,
        pos,
        paletteOpts,
      );
      group.name = `landmark-group-${lm.id}`;
      hitMesh.name = `landmark-${lm.id}`;
      (hitMesh as THREE.Mesh & { systemData?: MapSystem }).systemData = sys;
      group.visible = this.landmarksVisible;
      hitMesh.visible = this.landmarksVisible;
      group.renderOrder = 5;
      this.scene!.add(group);
      this.scene!.add(hitMesh);
      this.landmarkGroups.push(group);
      this.landmarkHitMeshes.push(hitMesh);
      this.landmarkLayers.push(layers);
    });
    this.syncLandmarkMeshesWithFactionSystems();
  }

  /**
   * Si un repère a le même nom qu'un système déjà rendu comme point faction/journal, on masque le groupe repère
   * pour éviter Sol « double ».
   */
  private syncLandmarkMeshesWithFactionSystems(): void {
    const list = this.buildMapPointsList();
    const names = new Set(list.map((s) => s.name.trim().toUpperCase()));
    for (let i = 0; i < GALACTIC_LANDMARKS.length; i++) {
      const lm = GALACTIC_LANDMARKS[i];
      const g = this.landmarkGroups[i];
      const h = this.landmarkHitMeshes[i];
      if (!g || !h) continue;
      const duplicate = names.has(lm.name.trim().toUpperCase());
      const show = this.landmarksVisible && !duplicate;
      g.visible = show;
      h.visible = show;
    }
  }

  private updateLandmarksVisibility(): void {
    this.syncLandmarkMeshesWithFactionSystems();
  }

  private updatePoints(preserveCamera = false): void {
    if (!this.scene || !this.camera || !this.controls) return;

    this.clearHoverFocus();
    this.hoveredSystem = null;
    this.tooltipData = null;
    if (this.renderer?.domElement?.style) this.renderer.domElement.style.cursor = 'default';

    this.systemGroups.forEach((g) => this.scene!.remove(g));
    this.systemGroups.clear();
    this.systemLayers.clear();
    this.hitMeshMap.forEach((m) => this.scene!.remove(m));
    this.hitMeshMap.clear();

    const list = this.buildMapPointsList();
    this.syncLandmarkMeshesWithFactionSystems();
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
      const baseR = hasFactionInfluenceOnMap(sys)
        ? this.radiusFromInfluence(sys.influencePercent)
        : STAR_MAP_NO_FACTION_BASE_R;
      const pos = new THREE.Vector3(ED_TO_SCENE_X(sys.coordsX!), sys.coordsY!, sys.coordsZ!);
      const { group, layers, hitMesh } = this.buildSystemVisualGroup(sys, baseR, pos);
      this.scene.add(group);
      this.systemGroups.set(sys.id, group);
      this.systemLayers.set(sys.id, layers);
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

  /**
   * Étoile centrale + disque d'influence + bordure catégorie + halo.
   * Le raycast utilise une sphère invisible de rayon = moitié du disque total (bord extérieur du halo).
   */
  private buildSystemVisualGroup(
    sys: MapSystem,
    baseR: number,
    pos: THREE.Vector3,
    opts?: { palette?: { core: number; influence: number } },
  ): { group: THREE.Group; layers: SystemVisualLayers; hitMesh: THREE.Mesh } {
    const group = new THREE.Group();
    group.position.copy(pos);
    group.userData['systemData'] = sys;
    group.name = `sys-${sys.id}`;

    const palette =
      opts?.palette ?? inferStarPalette(sys.primaryStarClass, sys.name);
    const coreR = Math.max(0.12, baseR * 0.12);
    /** Cercle billboard : le dégradé UV va à 0 sur le bord — pas de disque dur. */
    const starBillboardR = coreR * 2.5;

    const starGeom = new THREE.CircleGeometry(starBillboardR, 64);
    const starMat = createStarRadialMaterial(palette.core, STAR_ALPHA_SCALE);
    const starCore = new THREE.Mesh(starGeom, starMat);
    starCore.renderOrder = 4;

    const borderW = Math.max(0.04, baseR * RING_PRIMARY_BORDER_FRAC);
    const ring1Inner = baseR;
    const ring1Outer = ring1Inner + borderW;
    const diskR = ring1Inner * 0.94;
    const diskGeom = new THREE.CircleGeometry(diskR, 64);
    const diskMat = createInfluenceRadialMaterial(palette.influence, INFLUENCE_DISK_OPACITY);
    const influenceDisk = new THREE.Mesh(diskGeom, diskMat);
    influenceDisk.renderOrder = 2;
    const showInfDisk = hasFactionInfluenceOnMap(sys);
    influenceDisk.visible = showInfDisk;

    const cat = CATEGORY_COLORS[sys.mapCategory] ?? CATEGORY_NEON_CYAN;
    const ring1Geom = new THREE.RingGeometry(ring1Inner, ring1Outer, 64);
    const ring1Mat = new THREE.MeshBasicMaterial({
      color: cat,
      transparent: true,
      opacity: RING_PRIMARY_OPACITY,
      side: THREE.DoubleSide,
      depthWrite: false,
    });
    const ringPrimary = new THREE.Mesh(ring1Geom, ring1Mat);
    ringPrimary.renderOrder = 3;

    const r2Inner = ring1Outer + borderW * 0.25;
    const r2OuterFull = baseR * 1.28;
    /** Moitié de l'extension radiale de l'ancien halo → moins envahissant. */
    const r2Outer = r2Inner + (r2OuterFull - r2Inner) * 0.5;
    const ring2Geom = new THREE.RingGeometry(r2Inner, r2Outer, 48);
    const ring2Mat = new THREE.MeshBasicMaterial({
      color: cat,
      transparent: true,
      opacity: RING_SECONDARY_OPACITY,
      side: THREE.DoubleSide,
      depthWrite: false,
      blending: THREE.AdditiveBlending,
    });
    const ringSecondary = new THREE.Mesh(ring2Geom, ring2Mat);
    ringSecondary.renderOrder = 1;

    group.add(ringSecondary);
    group.add(influenceDisk);
    group.add(ringPrimary);
    group.add(starCore);

    const hitRadius = visualSystemDiskOuterRadius(baseR) * 0.5;
    const hitGeom = new THREE.SphereGeometry(hitRadius, 10, 8);
    const hitMat = new THREE.MeshBasicMaterial({ visible: false });
    const hitMesh = new THREE.Mesh(hitGeom, hitMat);
    hitMesh.position.copy(pos);
    (hitMesh as THREE.Mesh & { systemData?: MapSystem }).systemData = sys;
    hitMesh.name = `hit-${sys.id}`;

    const layers: SystemVisualLayers = { starCore, influenceDisk, ringPrimary, ringSecondary };
    return { group, layers, hitMesh };
  }

  private updateVisualHighlight(): void {
    if (!this.scene) return;
    const filter = this.systemsFilter;

    for (const [id, layers] of this.systemLayers) {
      const sys = this.systemGroups.get(id)?.userData?.['systemData'] as MapSystem | undefined;
      if (!sys) continue;
      if (this.hoveredSystem?.id === id) continue;

      const palette = inferStarPalette(sys.primaryStarClass, sys.name);
      const catHex = CATEGORY_COLORS[sys.mapCategory] ?? CATEGORY_NEON_CYAN;

      const starMat = layers.starCore.material as THREE.ShaderMaterial;
      const diskMat = layers.influenceDisk.material as THREE.ShaderMaterial;
      const r1Mat = layers.ringPrimary.material as THREE.MeshBasicMaterial;
      const r2Mat = layers.ringSecondary.material as THREE.MeshBasicMaterial;

      r1Mat.color.setHex(catHex);
      r2Mat.color.setHex(catHex);

      let coreHex = palette.core;
      let infHex = palette.influence;
      let coreAlpha = STAR_ALPHA_SCALE;
      let infOp = INFLUENCE_DISK_OPACITY;
      let r1Op = RING_PRIMARY_OPACITY;
      let r2Op = RING_SECONDARY_OPACITY;

      // Conflits : aligné sur le panneau (hasConflictState) — un même système peut être sain + conflit
      // ou avoir mapCategory « surveillance » / autre alors qu'il est en guerre ; le filtre carte doit quand même le mettre en avant.
      // « Systèmes sans nouvelles » : uniquement selon l'ancienneté Inara (> 30 j), pas la catégorie carte.
      const matchCat =
        filter === 'all' ||
        (filter === 'withoutNews'
          ? isInaraWithoutNewsCategory(sys)
          : filter === 'conflicts'
            ? hasConflictState(sys) || sys.mapCategory === 'conflicts'
            : (() => {
                const hc = FILTER_TO_CATEGORY[filter];
                return hc != null && sys.mapCategory === hc;
              })());

      coreHex = palette.core;
      infHex = palette.influence;

      if (!matchCat) {
        coreAlpha *= DIM_FACTOR_CORE;
        infOp *= DIM_FACTOR_INFLUENCE;
        r1Op *= DIM_FACTOR_RING1;
        r2Op *= DIM_FACTOR_RING2;
      }

      starMat.uniforms['uCoreColor'].value.setHex(coreHex);
      starMat.uniforms['uAlphaScale'].value = coreAlpha;
      diskMat.uniforms['uTint'].value.setHex(infHex);
      diskMat.uniforms['uAlphaScale'].value = infOp;
      r1Mat.opacity = r1Op;
      r2Mat.opacity = r2Op;
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
      const group = this.systemGroups.get(this.hoveredSystem.id);
      const layers = this.systemLayers.get(this.hoveredSystem.id);
      if (group) group.scale.setScalar(1);
      if (layers) {
        (layers.starCore.material as THREE.ShaderMaterial).depthTest = true;
        (layers.influenceDisk.material as THREE.ShaderMaterial).depthTest = true;
      }
      this.updateVisualHighlight();
    }
  }

  private applyHoverFocus(systemId: number): void {
    const group = this.systemGroups.get(systemId);
    const layers = this.systemLayers.get(systemId);
    if (!group || !layers) return;
    group.scale.setScalar(HOVER_SCALE);
    const sys = group.userData['systemData'] as MapSystem | undefined;
    const sm = layers.starCore.material as THREE.ShaderMaterial;
    const dm = layers.influenceDisk.material as THREE.ShaderMaterial;
    const r1 = layers.ringPrimary.material as THREE.MeshBasicMaterial;
    const r2 = layers.ringSecondary.material as THREE.MeshBasicMaterial;
    const pal = inferStarPalette(sys?.primaryStarClass, sys?.name ?? '');
    const hoverCore = hoverStarCoreHex(pal.core);
    sm.uniforms['uCoreColor'].value.setHex(hoverCore);
    sm.uniforms['uAlphaScale'].value = Math.min(1.25, STAR_ALPHA_SCALE + 0.22);
    sm.depthTest = false;
    if (sys && hasFactionInfluenceOnMap(sys)) {
      dm.uniforms['uAlphaScale'].value = INFLUENCE_DISK_HOVER_OPACITY;
    }
    r1.opacity = Math.min(1, RING_PRIMARY_OPACITY + 0.1);
    r2.opacity = Math.min(0.28, RING_SECONDARY_OPACITY + 0.08);
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

  /** Double-clic : recentre la vue Orbit sur le système sous le curseur (sans changer la logique de clic simple). */
  private onDblClick(event: MouseEvent): void {
    if (!this.raycaster || !this.camera || !this.controls) return;
    event.preventDefault();
    const rect = this.renderer!.domElement.getBoundingClientRect();
    const nx = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    const ny = -((event.clientY - rect.top) / rect.height) * 2 + 1;
    this.raycaster.setFromCamera(new THREE.Vector2(nx, ny), this.camera);
    const hitMeshes = Array.from(this.hitMeshMap.values());
    const landmarkPick = this.landmarkHitMeshes.filter((m) => m.visible);
    const hits = this.raycaster.intersectObjects([...hitMeshes, ...landmarkPick], false);
    const hit = hits[0];
    if (!hit?.object) return;
    const obj = hit.object;
    if (obj.name.startsWith('landmark-')) {
      this.centerCameraOnWorldPosition((obj as THREE.Mesh).position);
      return;
    }
    const sys: MapSystem | null = (obj as THREE.Mesh & { systemData?: MapSystem }).systemData ?? null;
    if (!sys) return;
    this.centerCameraOnSystem(sys.id);
  }

  /** Double-clic sur le panneau libellé (overlay) : même centrage que le double-clic sur le repère 3D. */
  centerMapOnLandmark(landmarkId: string, event?: MouseEvent): void {
    event?.preventDefault();
    event?.stopPropagation();
    const idx = GALACTIC_LANDMARKS.findIndex((lm) => lm.id === landmarkId);
    if (idx < 0) return;
    const pos = this.landmarkPositions[idx];
    if (!pos) return;
    this.centerCameraOnWorldPosition(pos.clone());
  }

  /** Pivot Orbit + caméra sur un point monde (repères galactiques, etc.). */
  private centerCameraOnWorldPosition(pos: THREE.Vector3): void {
    if (!this.camera || !this.controls) return;
    this.controls.target.copy(pos);
    const dist = 80;
    this.camera.position.set(pos.x + dist, pos.y + dist, pos.z + dist);
    const orbitDist = this.camera.position.distanceTo(this.controls.target);
    this.viewDistance = orbitDist;
    this.zoomLevel = zoomLevelForViewDistance(orbitDist);
    this.cdr.markForCheck();
  }

  /** Pivot Orbit + caméra sur le système (même recul que le focus synchronisé panneau). */
  private centerCameraOnSystem(id: number): void {
    const group = this.systemGroups.get(id);
    if (!group) return;
    this.centerCameraOnWorldPosition(group.position);
  }

  private onMouseLeave(): void {
    this.clearHoverFocus();
    this.hoveredSystem = null;
    this.tooltipData = null;
    if (this.renderer?.domElement?.style) this.renderer.domElement.style.cursor = 'default';
  }

  private focusOnSystemId(id: number): void {
    this.centerCameraOnSystem(id);
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
    if (this.camera) {
      const camPos = this.camera.position;
      if (this.systemLayers.size > 0) {
        for (const layers of this.systemLayers.values()) {
          layers.starCore.lookAt(camPos);
          layers.influenceDisk.lookAt(camPos);
          layers.ringPrimary.lookAt(camPos);
          layers.ringSecondary.lookAt(camPos);
        }
      }
      for (const layers of this.landmarkLayers) {
        layers.starCore.lookAt(camPos);
        layers.influenceDisk.lookAt(camPos);
        layers.ringPrimary.lookAt(camPos);
        layers.ringSecondary.lookAt(camPos);
      }
    }
    if (this.landmarksVisible && this.camera && this.renderer && this.landmarkPositions.length > 0) {
      const rect = this.renderer.domElement.getBoundingClientRect();
      const w = rect.width;
      const h = rect.height;
      const labels: {
        id: string;
        name: string;
        region?: string;
        distanceFromSolLy: number;
        x: number;
        y: number;
        visible: boolean;
      }[] = [];
      const v = new THREE.Vector3();
      for (let i = 0; i < GALACTIC_LANDMARKS.length; i++) {
        v.copy(this.landmarkPositions[i]);
        v.project(this.camera!);
        const frustumOk = v.z < 1 && v.x >= -1 && v.x <= 1 && v.y >= -1 && v.y <= 1;
        const meshShown = this.landmarkHitMeshes[i]?.visible === true;
        const x = ((v.x + 1) / 2) * w + rect.left;
        const y = ((1 - v.y) / 2) * h + rect.top + LANDMARK_LABEL_OFFSET_Y_PX;
        const lm = GALACTIC_LANDMARKS[i];
        labels.push({
          id: lm.id,
          name: lm.name,
          region: 'region' in lm ? lm.region : undefined,
          distanceFromSolLy: landmarkDistanceFromSolLy(lm),
          x,
          y,
          visible: frustumOk && meshShown,
        });
      }
      this.landmarkLabels = labels;
      this.cdr.markForCheck();
    } else if (this.landmarkLabels.length > 0) {
      this.landmarkLabels = [];
      this.cdr.markForCheck();
    }
    this.renderer?.render(this.scene!, this.camera!);
  };

  /** Distance au Sol (AL), libellé pour le panneau repère. */
  formatLandmarkDistanceFromSol(ly: number): string {
    const n = Math.round(ly);
    return `${n.toLocaleString('fr-FR')} AL depuis Sol`;
  }

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

/**
 * Priorité décroissante : si un même système (même `id`) est présent dans plusieurs listes API
 * (ex. origine + siège), une seule entrée carte doit être créée — sinon deux groupes 3D se superposent
 * (effet « double disque » / anneaux doublés).
 */
const GUILD_CATEGORY_PRIORITY: { cat: MapCategoryKey; key: keyof GuildSystemsResponseInput }[] = [
  { cat: 'headquarter', key: 'headquarter' },
  { cat: 'surveillance', key: 'surveillance' },
  { cat: 'conflicts', key: 'conflicts' },
  { cat: 'critical', key: 'critical' },
  { cat: 'origin', key: 'origin' },
  { cat: 'low', key: 'low' },
  { cat: 'healthy', key: 'healthy' },
  { cat: 'others', key: 'others' },
];

/** Systèmes guilde enrichis (coords EDSM). */
function guildInputToFactionMapSystems(input: GuildSystemsResponseInput): MapSystem[] {
  const byId = new Map<number, MapSystem>();
  for (const { cat, key: arrKey } of GUILD_CATEGORY_PRIORITY) {
    for (const s of input[arrKey] ?? []) {
      if (s.coordsX == null || s.coordsY == null || s.coordsZ == null) continue;
      if (byId.has(s.id)) continue;
      byId.set(s.id, {
        ...s,
        coordsX: s.coordsX,
        coordsY: s.coordsY,
        coordsZ: s.coordsZ,
        mapCategory: cat,
      });
    }
  }
  return Array.from(byId.values());
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
