// Shared types for the .cs1x reader, the mapping and the import package (schema/README.md, v1).
// Pure TypeScript (no DOM, no Node APIs): runs in the browser and directly in Node via type stripping.

export type Vec3 = [number, number, number];
export type Bezier = [Vec3, Vec3, Vec3, Vec3];
// Raw CS1 field bags (NetInfo.m_*, lane m_*): whatever the exporter wrote.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export type Fields = Record<string, any>;

export interface Cs1Node {
  id: number;
  info: string;
  position: Vec3;
  flags?: string;
  elevation?: number;
  heightAboveTerrain?: number;
  building?: number;
  segments?: number[];
}

export interface Cs1Segment {
  id: number;
  info: string;
  ai?: string;
  startNode: number;
  endNode: number;
  startDirection?: Vec3;
  endDirection?: Vec3;
  bezier: Bezier;
  flags?: string;
  inverted?: boolean;
  length: number;
  oneWay?: boolean;
  elevationKind?: string;
}

export interface Cs1LaneProp {
  prop?: string | null;
  tree?: string | null;
  finalProp?: string | null;
  finalTree?: string | null;
  fields?: Fields;
}

export interface Cs1Lane {
  index?: number;
  fields: Fields;
  props: Cs1LaneProp[];
}

export interface NetInfo {
  name: string;
  title?: string;
  ai?: string;
  service?: string;
  subService?: string;
  level?: string;
  className?: string;
  fields: Fields;
  aiFields?: Fields;
  /** On a variant: the ground NetInfo's name. null on a ground NetInfo. */
  parent?: string | null;
  /** On a ground NetInfo: elevated / bridge / slope / tunnel variant names. */
  variants?: Record<string, string>;
  lanes: Cs1Lane[];
}

export interface ImtFiller {
  markingType: 'Node' | 'Segment' | string;
  markingId: number;
  fillerId?: number;
  isMedian?: boolean;
  styleClass?: string;
  styleType?: string;
  color?: [number, number, number, number] | number[];
  styleProperties?: Fields;
  polygon: Vec3[];
}

export interface Terrain {
  resolution: number;
  size: number;
  heightScale: number;
  min?: number;
  max?: number;
  seaLevel?: number | null;
  /** resolution × resolution raw values, row 0 = south (min z), column 0 = west (min x). */
  heights: Uint16Array;
}

export type SectionStatus = 'ok' | 'failed' | 'absent' | string;

export interface Manifest {
  format: string;
  formatVersion: number;
  exporter?: { name?: string; version?: string };
  exportedAt?: string;
  city?: { name?: string; saveName?: string };
  game?: { version?: string };
  mods?: { name: string; version?: string }[];
  coordinateSystem?: string;
  mapSize?: number;
  sections?: Record<string, SectionStatus>;
}

export interface Cs1ReportItem {
  level: 'info' | 'warning' | 'error' | string;
  section?: string;
  message: string;
}

/** A loaded CS1 city, whatever the source (.cs1x or the M0 network dump). */
export interface Cs1City {
  source: 'cs1x' | 'm0-dump';
  manifest: Manifest;
  nodes: Cs1Node[];
  segments: Cs1Segment[];
  netInfos: NetInfo[];
  imtFillers: ImtFiller[];
  terrain: Terrain | null;
  /** The exporter's own report plus anything the reader noticed. */
  report: Cs1ReportItem[];
  /** sha256 of the source file bytes (hex), when computed. */
  sha256: string | null;
}

// ---------------------------------------------------------------- import package (v1)

export interface Crop {
  centerX: number;
  centerZ: number;
  size: number;
}

export type PrefabType = 'RoadPrefab' | 'RoadBuilderPrefab' | 'TrackPrefab' | 'PathwayPrefab';

export interface PlanPrefab {
  type: PrefabType;
  name: string;
  /** CS1 ground NetInfo this prefab stands for. */
  source: string;
  /** IMT lane overrides baked into this RB config, e.g. "6=bus,8.5=bus". */
  imt?: string;
}

export interface PlanNode {
  id: number;
  x: number;
  z: number;
  y: number;
  heightAboveTerrain: number;
  elevation: number;
}

export type SegmentKind = 'ground' | 'elevated' | 'bridge' | 'slope' | 'tunnel';

export interface PlanSegment {
  id: number;
  prefab: string;
  start: number;
  end: number;
  bezier: Bezier;
  elevationStart: number;
  elevationEnd: number;
  kind: SegmentKind;
  /** true when start/end/bezier were swapped relative to CS1 because the CS1 segment has the Invert flag. */
  reversed?: boolean;
  /** CS1 NetInfo of the segment (variant name for elevated/bridge/slope/tunnel), for traceability. */
  cs1Info?: string;
}

export interface Plan {
  format: 'cs1conv-plan';
  formatVersion: 1;
  generator?: { name: string; version: string; generatedAt: string };
  source: { city: string; saveName: string; exportedAt: string; cs1xSha256: string | null; kind?: string };
  crop: Crop;
  transform: { dx: number; dz: number; heightMode: 'terrain-relative' };
  /** CS2 bounds of the crop after the transform. */
  bounds?: { minX: number; maxX: number; minZ: number; maxZ: number };
  prefabs: Record<string, PlanPrefab>;
  nodes: PlanNode[];
  segments: PlanSegment[];
  stats: PlanStats;
}

export interface PlanStats {
  segments: number;
  skipped: number;
  nodes: number;
  /** Segments of the whole city outside the crop (not counted in skipped). */
  outsideCrop: number;
  /** Segments with exactly one node inside the crop (counted in skipped). */
  crossingCropEdge: number;
  /** Crop segments whose network is not placeable / not mapped (counted in skipped). */
  unmapped: number;
  rbConfigs: number;
  byPrefabType: Record<string, number>;
}

export type PackageReportKind =
  | 'imt-node-fill'
  | 'imt-partial-fill'
  | 'imt-paint'
  | 'imt-override'
  | 'unmapped-netinfo'
  | 'not-placeable'
  | 'outside-crop'
  | 'crossing-crop-edge'
  | 'width-loss'
  | 'lane-approximation'
  | 'crop'
  | 'source'
  | 'mapping-override';

export interface PackageReportItem {
  level: 'info' | 'warning';
  kind: PackageReportKind;
  message: string;
  cs1?: Record<string, number | string | number[]>;
  count?: number;
}

export interface PackageReport {
  format: 'cs1conv-report';
  formatVersion: 1;
  items: PackageReportItem[];
}
