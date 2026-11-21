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

// ---------------------------------------------------------------- M2 sections (schema/README.md §1)

export interface ZoneBlock {
  id: number;
  position: Vec3;
  /** CS1 ZoneBlock.m_angle, radians. */
  angle: number;
  rows: number;
  segment: number;
  /** rows × 4 chars, row-major: hex zone code per cell, "-" = invalid cell. */
  zones: string;
  occupied?: string;
}
export interface ZonesSection { cellSize: number; columns: number; zoneCodes: Record<string, string>; blocks: ZoneBlock[] }

export interface WaterSource {
  index?: number; id?: number;
  type: 'natural' | 'facility' | 'cleaner' | string;
  inputPosition: Vec3; outputPosition: Vec3;
  inputRate?: number; outputRate?: number;
  target?: number; targetHeight?: number;
  flow?: number; water?: number; pollution?: number; terrainHeightAtOutput?: number;
}
export interface WaterSection {
  seaLevel: number | null;
  nextSeaLevel?: number | null;
  depth?: { file?: string; resolution: number; size: number; heightScale: number; wetSamples?: number; maxDepth?: number };
  sources: WaterSource[];
  /** Decoded water.r16 (depth above terrain, raw units), when present. */
  depthGrid?: Uint16Array;
}

export interface TransitStop {
  node: number; position: Vec3;
  lane?: number; segment?: number; laneIndex?: number; laneOffset?: number; building?: number;
}
export interface TransitLine {
  id: number; name?: string; number?: number; info?: string; type: string; vehicleType?: string;
  color?: number[]; customColor?: boolean; complete?: boolean; vehicles?: number; flags?: string;
  stops: TransitStop[];
}
export interface TransitSection { lines: TransitLine[] }

export interface TmpeLaneRef { lane?: number; segment: number; laneIndex: number; node?: number }
export interface TmpeSection {
  assembly?: { name?: string; version?: string };
  laneArrows?: (TmpeLaneRef & { arrows: string[] })[] | null;
  laneConnections?: { sourceSegment: number; sourceLaneIndex: number; targetSegment: number; targetLaneIndex: number; sourceStartNode?: boolean; node: number; group?: string }[] | null;
  speedLimits?: (TmpeLaneRef & { kmh: number; unlimited?: boolean })[] | null;
  defaultSpeedLimits?: Record<string, number> | null;
  vehicleRestrictions?: (TmpeLaneRef & { allowed: string[] })[] | null;
  junctionRestrictions?: ({ segment: number; startNode?: boolean; node: number } & Record<string, unknown>)[] | null;
  prioritySigns?: { segment: number; node: number; type: 'Main' | 'Stop' | 'Yield' | string }[] | null;
  parkingRestrictions?: { segment: number; forwardParkingAllowed?: boolean; backwardParkingAllowed?: boolean }[] | null;
  trafficLights?: { node: number; trafficLight: boolean }[] | null;
  timedTrafficLights?: { nodeId: number; nodeGroup?: number[] }[] | null;
  parts?: Record<string, string>;
}

export interface NcSegmentEnd { segment: number; isStartNode?: boolean; noMarkings?: boolean | null; [k: string]: unknown }
export interface NcNode {
  id: number; type: string; defaultType?: string; mode?: string; offset?: number | null; noMarkings?: boolean | null;
  segmentEnds?: NcSegmentEnd[]; [k: string]: unknown;
}
export interface NodeControllerSection { assembly?: { name?: string; version?: string }; nodes: NcNode[] }

// ---------------------------------------------------------------- M3 sections (schema/README.md §1)

export interface Cs1Building {
  id: number; info: string; workshopId?: string | null; position: Vec3;
  /** degrees, CS1 Building.m_angle × 180/π */
  angle: number;
  service?: string; subService?: string; level?: number; ai?: string; growable: boolean;
  /** footprint in 8 m cells */
  width: number; length: number; size?: number[];
  parent?: number; subBuilding?: number; netNode?: number;
  accessSegment?: number; district?: number; park?: number; flags?: string; customName?: string;
}
export interface BuildingsSection { angleUnit?: string; buildings: Cs1Building[] }

export interface Cs1Asset {
  name: string; kind?: string; workshopId?: string | null; custom?: boolean; title?: string; count: number;
  service?: string; subService?: string; zone?: string | null; level?: string; ai?: string; growable?: boolean;
  footprint?: number[]; size?: number[]; height?: number; placementMode?: string; subBuildings?: number;
  tags?: string[]; aiFields?: Fields;
}
/** props.json / trees.json (schema §1): free-standing instances, rows [infoIndex, x, y, z, angle, fixedHeight] / [infoIndex, x, y, z, fixedHeight]. */
export interface PropsSection { props: number[][]; infos: string[] }
export interface TreesSection { trees: number[][]; infos: string[] }
export interface AssetsSection { buildings: Cs1Asset[]; props?: Cs1Asset[]; trees?: Cs1Asset[]; networks?: Cs1Asset[] }

export interface Cs1District { id: number; name: string; nameLocation?: Vec3; cells?: number; area?: number; policies?: string[] }
export interface Cs1ParkArea { id: number; name: string; type?: string; kind?: string; level?: string; mainGate?: number; cells?: number; area?: number; policies?: string[] }
export interface DistrictsSection {
  grid: { resolution: number; cellSize: number; size: number; districtFile?: string; parkFile?: string };
  city?: { policies?: string[] };
  districts: Cs1District[];
  parks: Cs1ParkArea[];
  /** Decoded rasters (resolution², row 0 = south), when present. */
  districtGrid?: Uint8Array;
  parkGrid?: Uint8Array;
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
  zones?: ZonesSection | null;
  water?: WaterSection | null;
  transit?: TransitSection | null;
  tmpe?: TmpeSection | null;
  nodeController?: NodeControllerSection | null;
  buildings?: BuildingsSection | null;
  assets?: AssetsSection | null;
  districts?: DistrictsSection | null;
  props?: PropsSection | null;
  trees?: TreesSection | null;
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
  /** Lane overrides baked into this RB config, e.g. "6=bus,8.5=bus" (field name from M1; since M2 also from TM:PE). */
  imt?: string;
  /** Where the lane overrides come from: "IMT", "TM:PE" or "IMT+TM:PE". */
  lanesFrom?: string;
  /** Speed limit of this RB variant when it differs from the CS1 road's default (TM:PE). */
  speedKmh?: number;
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
  /** CS2 mods the plan needs besides Road Builder (e.g. "Traffic" for lane connections and priority signs). */
  requires?: string[];
  zoning?: PlanZoning;
  buildings?: PlanBuildings;
  districts?: PlanDistricts;
  objects?: PlanObjects;
  surfaces?: PlanSurfaces;
  water?: PlanWater;
  transit?: PlanTransit;
  traffic?: PlanTraffic;
  terrain?: PlanTerrainRef;
  outsideConnections?: PlanOutsideConnections;
}

/** Placed buildings (M3): service buildings, parks, landmarks … matched to CS2 prefabs. Growables are left to zoning. */
export interface PlanBuildingItem {
  /** CS1 building id */
  id: number;
  cs1Info: string;
  type: 'BuildingPrefab';
  prefab: string;
  /** match score 0–1; null when the user chose the prefab */
  score: number | null;
  family: string | null;
  x: number; z: number;
  /** closest point of the access road (plan coordinates), null without an access road */
  front: [number, number] | null;
  /** unit vector from the building towards its road */
  facing: [number, number] | null;
  lot: number[] | null;
  cs1Lot: [number, number];
  /** CS1 segment id of the access road (a plan segment), 0 if none */
  accessSegment: number;
  /** next-best CS2 prefabs, tried in order when the first does not fit; empty when the user chose the prefab */
  alternatives: string[];
  /** CS1 district id (plan.districts) */
  district?: number;
  /** what the placer does when the building does not fit at its spot: nearby (≤ 400 m), anywhere in the crop, or skip */
  fallback?: 'nearby' | 'anywhere' | 'skip';
}
export interface PlanBuildings {
  generator: { matcher: string; web: string };
  catalogue: { game: string | null; prefabs: number };
  cs1AngleConvention: { facing: string; agreement: number | null; buildingsWithRoad: number };
  items: PlanBuildingItem[];
  stats: {
    items: number; withRoad: number; withoutRoad: number; growablesInCrop: number;
    skipped: { outsideCrop: number; subBuilding: number; growable: number; skip: number; noEquivalent: number; roadNotImported: number };
  };
}

/** CS1 districts in the crop as CS2 district areas (outer outlines in plan coordinates). */
export interface PlanDistricts {
  items: { cs1Id: number; name: string; outlines: [number, number][][]; policies: string[]; cs1Policies: string[]; area: number }[];
  /** CS2 policies for the whole city (from CS1 city-wide policies). */
  cityPolicies: string[];
}

/** Free-standing trees and rocks (buildings/lib/objects.mjs buildObjectsSection, mapped by match/vegetation.mjs). */
export interface PlanObjects {
  generator: { vegetation: string };
  prefabs: string[];
  /** [prefabIndex, x, z, angleDegrees, age]; age = CS2 ObjectDefinition.m_Age (0.3–0.55 = adult) */
  items: [number, number, number, number, number][];
  stats: {
    items: number;
    skipped: { outsideCrop: number; noEquivalent: number };
    byType: Record<string, { cs2: string | null; decision: string; reason: string; count: number }>;
  };
}

/** IMT junction fills with a CS2 surface (buildings/lib/objects.mjs buildSurfacesSection). */
export interface PlanSurfaces {
  items: { cs1Node: number; styleType: string; type: 'SurfacePrefab'; prefab: string; polygon: [number, number][] }[];
  /** "styleType rgba(…)" → count of fills without a CS2 surface (paint) */
  notConverted: Record<string, number>;
  stats: { items: number };
}

/** Zoned cells: centre of every zoned CS1 cell (CS2 coordinates) → index into prefabs (CS2 zone prefab names). */
export interface PlanZoning {
  theme: string;
  cellSize: number;
  prefabs: string[];
  /** [x, z, prefabIndex] */
  cells: [number, number, number][];
  stats: { cells: number; blocks: number; outsideCrop: number; roadNotInPlan: number };
}

export interface PlanWater {
  /** CS1 sea level, absolute metres (terrain datum). */
  seaLevel: number | null;
  /** Share of the crop under water in the CS1 snapshot (0–1), null without a depth grid. */
  wetFraction: number | null;
  maxDepth: number;
  /** Natural CS1 water sources in the crop (CS2 coordinates). */
  sources: { cs1Index: number; x: number; z: number; y: number; targetHeight: number | null; outputRate: number | null }[];
  /** Exactly what terrain/mapprep.mjs "water" sends to MapPrep water.set: sea level + CS2 sources (natural + inflow/lake). */
  apply?: { seaLevel: number | null; sources: { x: number; z: number; height: number; radius: number; polluted: number; cs1: Record<string, unknown> }[] };
  /** CS1 depth-grid checkpoints (100 wet + 100 dry) for a wet/dry check after the water settles. */
  checkpoints?: { wet: { x: number; z: number; cs1Depth: number }[]; dry: { x: number; z: number; cs1Depth: number }[]; totals: { wet: number; dry: number } };
}

/** Pointer to the package's terrain files (terrain/terrain.json), filled when the package is written. */
export interface PlanTerrainRef {
  dir: string; meta: string; heightScale: number; heightOffset: number; heightMode: string;
  /** apply.mjs "auto" sea level for a city without water: floor(min) − 20 */
  seaLevelDry: number; min: number; max: number; world: boolean;
}

/** Outside connections where the imported network leaves the crop (terrain/lib/connections.mjs). */
export interface PlanOutsideConnections {
  connections: { node: number; x: number; z: number; type: 'road' | 'train'; info: string; cs1Segments: number[]; outward: [number, number] }[];
  skipped: { cs1Segment: number; node: number; info: string; reason: string }[];
  counts: { crossing: number; typed: number; connections: number; road: number; train: number };
}

export interface PlanTransitLine {
  cs1Id: number;
  name: string;
  number: number | null;
  cs1Type: string;
  /** CS2 transport line prefab, e.g. "Bus Line". */
  line: string;
  /** CS2 kerbside stop prefab, or null for station-based lines. */
  stopPrefab: string | null;
  color: string | null;
  vehicles: number | null;
  /** true for lines whose stops are stations (train, metro, ship, plane). */
  requiresStations: boolean;
  /** true when stops outside the crop / off the imported roads were dropped. */
  truncated: boolean;
  stops: { cs1Node: number; x: number; z: number; y: number; segment: number | null; side: 'left' | 'right' | null; t: number | null; cs1Building: number }[];
}
export interface PlanTransit { lines: PlanTransitLine[] }

export interface PlanLane {
  segment: number; ordinal: number; count: number; offset: number; dir: 'forward' | 'backward' | 'both'; cs1LaneIndex: number;
}
export interface PlanTraffic {
  requires: string[];
  /** Every junction in the plan with its CS1 traffic-light state. */
  nodes: { id: number; trafficLights: boolean; allWayStop?: boolean }[];
  segmentEnds: { segment: number; node: number; priority?: 'main' | 'stop' | 'yield'; forbid?: ('left' | 'straight' | 'right')[]; crosswalk?: boolean; uturn?: boolean }[];
  laneConnections: { node: number; from: PlanLane; to: PlanLane }[];
  laneArrows: { node: number; lane: PlanLane; arrows: ('left' | 'straight' | 'right')[] }[];
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
  | 'mapping-override'
  | 'speed-limit'
  | 'zoning'
  | 'water'
  | 'transit'
  | 'traffic'
  | 'node-controller'
  | 'buildings'
  | 'districts'
  | 'outside-connections'
  | 'objects'
  | 'surfaces';

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
