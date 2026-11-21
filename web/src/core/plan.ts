// Build the import package (schema/README.md §2): cs1conv-plan.json + roadbuilder/*.json + report.json.

import { bestCropCenter, cropBounds, cropTransform, inCrop, validateCrop } from './crop.ts';
import { classifyFiller, laneCoverage, overrideSignature, wholeLaneOverrides } from './imt.ts';
import type { LaneOverrideKind } from './imt.ts';
import { categorize, defaultHeightMode, foldHeightMode, prefabKeyVanilla, resolveTarget } from './mapping.ts';
import type { MappingOverride, NetCategory, ResolvedTarget } from './mapping.ts';
import { analyse, convert, netInfoTitle } from './rb.ts';
import type { RbConfig, RbReport } from './rb.ts';
import { packageReadme } from './readme.ts';
import { APP_VERSION } from './docs.ts';
import { conversionSummary } from './summary.ts';
import { fmtNumber, translator } from './i18n/index.ts';
import type { Lang } from './i18n/index.ts';
import type { ConvCategory } from './summary.ts';
import type { PlanContext } from './m2/context.ts';
import { buildNodeController } from './m2/nodecontroller.ts';
import { buildTraffic, trafficLaneVariants } from './m2/traffic.ts';
import { buildTransit } from './m2/transit.ts';
import { buildWater } from './m2/water.ts';
import { DEFAULT_ZONING, buildZoning } from './m2/zoning.ts';
import { deriveConnections } from './terrain/connections.ts';
import { cs1Heights } from './terrain/heightmap.ts';
import { terrainFiles } from './terrain/package.ts';
import { convertWater, depthCheckpoints, regionSources } from './terrain/water.ts';
import type { ZoningOptions } from './m2/zoning.ts';
import { buildBuildings } from './m3/buildings.ts';
import type { AssetMatch, AssetOverrides, Cs2Catalogue, FallbackOverrides, PlacementFallback } from './m3/buildings.ts';
import { buildDistricts } from './m3/districts.ts';
import { addDefaultDepots } from '../../../buildings/lib/plan.mjs';
import { buildObjects, buildSurfaces, SURFACE_MAP } from './m3/objects.ts';
import { matchVanilla } from './vanilla.ts';
import { buildUtilities } from './m3/utilities.ts';
import { alignNcShifts } from './m2/ncalign.ts';
import type { Cs2RoadCatalogue, VanillaMatch } from './vanilla.ts';
import type {
  Bezier, Crop, Cs1City, Cs1Node, Cs1Segment, NetInfo, PackageReport, PackageReportItem, Plan, PlanNode, PlanPrefab, PlanSegment, SegmentKind,
  PlanTransit,
} from './types.ts';
import { HEIGHT_MODES } from './types.ts';
import type { HeightMode } from './types.ts';
import { RAIL_DEPOT_TYPES } from './types.ts';

export const GENERATOR = { name: 'cs1conv-web', version: APP_VERSION };

export interface BuildOptions {
  crop: Crop;
  /** Language of the "What converts" summaries and the package README (default en). report.json is always English. */
  lang?: Lang;
  /** Per ground-NetInfo mapping overrides (key: NetInfo name). */
  overrides?: Record<string, MappingOverride>;
  generatedAt?: string;
  /** M2: zoning (theme, densities); omitted = DEFAULT_ZONING. */
  zoning?: ZoningOptions;
  /** M2: TM:PE rules, speed limits, bus lanes and traffic lights (default on). */
  traffic?: boolean;
  /** M2: transit lines (default on). */
  transit?: boolean;
  /** M2: sea level and water sources (default on). */
  water?: boolean;
  /** M3: place service buildings, parks and landmarks (default on; needs a catalogue). */
  buildings?: boolean;
  /** M3: the CS2 building catalogue (cs2-catalogue.json); without it buildings are not matched. */
  catalogue?: Cs2Catalogue | null;
  /** M3: per CS1 building type, a CS2 prefab name or "skip". */
  assetOverrides?: AssetOverrides;
  /** M3: per-type placement fallback (Assets tab); others get `assetFallback`, else skip (docs/DECISIONS.md #106). */
  assetFallbacks?: FallbackOverrides;
  assetFallback?: PlacementFallback;
  /** Vanilla CS2 road layouts (cs2-roads.json); with preferVanilla, a road whose generated profile matches one uses it. */
  roads?: Cs2RoadCatalogue | null;
  /** Use a matching vanilla CS2 road instead of a generated Road Builder road (default true; needs `roads`). */
  preferVanilla?: boolean;
  /** Metro / train yards: at the crop edge (default) or placed by the user (plan.transit.depotMode). */
  depotMode?: PlanTransit['depotMode'];
  /** M3: districts and policies (default on). */
  districts?: boolean;
  /** M3: free-standing trees and rocks (default on; needs a catalogue with CS2 trees). */
  objects?: boolean;
  /** M3: IMT junction fills (pavement, grass, gravel) as CS2 surfaces (default on). */
  surfaces?: boolean;
  /** Outside connections where highways and train tracks leave the crop (default on). */
  outsideConnections?: boolean;
  /** Height mode per CS1 ground NetInfo (mapping table); others get `heightMode`, else their default (#113). */
  heightModes?: Record<string, HeightMode>;
  heightMode?: HeightMode;
  /** Burn the conform roads into the heightmap when the package is written (default on; #110). */
  roadConform?: boolean;
  /** M3 (#115): the player's power lines and pipes away from the converted roads → plan.utilities (default on). */
  utilities?: boolean;
  /** M3 (#128): power plants, pumps and sewage buildings may move up to 400 m if they don't fit (default off). */
  utilitiesNearby?: boolean;
}

export interface MappingRow {
  netInfo: string;
  title: string;
  category: NetCategory;
  ai: string;
  /** Every used CS1 NetInfo folded into this row (the ground info and its variants). */
  infos: string[];
  segments: number;
  segmentsInCrop: number;
  target: ResolvedTarget;
  override?: MappingOverride;
  /** Plan prefab keys this row produced in the crop. */
  prefabKeys: string[];
  /** Base RB config (no IMT overrides) for rb rows. */
  rb?: RbReport;
  /** For rb rows when vanilla roads are preferred: the matching vanilla road of the base profile, or why none fits. */
  vanilla?: VanillaMatch;
  /** How its ground segments meet the terrain, and the default for the type (#110–113). */
  heightMode: HeightMode;
  heightModeAuto: HeightMode;
}

export interface RbVariant extends RbReport { key: string; segments: number }

export interface BuiltPackage {
  plan: Plan;
  /** Language of summary and README.txt. */
  lang: Lang;
  report: PackageReport;
  rbConfigs: RbConfig[];
  rbVariants: RbVariant[];
  mapping: MappingRow[];
  errors: string[];
  /** What converts per category (filled by buildPackage). */
  summary: ConvCategory[];
  /** Per-category entries added by the M2 sections (zoning, traffic, transit, water...). */
  extraSummary?: ConvCategory[];
  /** M3: every CS1 building type with its CS2 match (for the Assets tab); empty without a catalogue. */
  assetMatches?: AssetMatch[];
  /** BuildOptions.roadConform, for completePackage */
  roadConform?: boolean;
  /** #128: utility facilities placed from the crop (null without any) and whether they may move nearby */
  utilityFacilities?: { types: string[]; buildings: number; moving: number; optedIn: boolean } | null;
}

const r3 = (v: number) => Math.round(v * 1000) / 1000;
const r2 = (v: number) => Math.round(v * 100) / 100;

/** Kind of a segment from the exporter's elevationKind and the NetInfo. */
export function segmentKind(seg: Cs1Segment, info: NetInfo | undefined): SegmentKind {
  const name = info?.name ?? seg.info, ai = info?.ai ?? seg.ai ?? '';
  const ek = seg.elevationKind ?? '';
  if (ek === 'slope' || /Slope/.test(name)) return 'slope';
  if (ek === 'tunnel' || ek === 'underground' || /Tunnel/.test(ai)) return 'tunnel';
  if (ek === 'bridge' || ek === 'elevated' || /Bridge/.test(ai)) return /Bridge/.test(name) ? 'bridge' : 'elevated';
  return 'ground';
}

/** Course elevation at a node for a segment of the given kind (height above terrain, m). */
export function nodeElevation(kind: SegmentKind, node: Cs1Node | undefined): number {
  if (kind === 'ground' || !node) return 0;
  const h = Number(node.heightAboveTerrain ?? node.elevation ?? 0);
  const v = kind === 'elevated' || kind === 'bridge' ? Math.max(0, h) : kind === 'tunnel' ? Math.min(0, h) : h;
  return Math.abs(v) < 0.05 ? 0 : r2(v);
}

export function buildPackage(city: Cs1City, opts: BuildOptions): BuiltPackage {
  const { crop } = opts;
  const overrides = opts.overrides ?? {};
  const items: PackageReportItem[] = [];
  const cv = validateCrop(crop, city.manifest.mapSize);
  for (const w of cv.warnings) items.push({ level: 'warning', kind: 'crop', message: w });
  if (city.source === 'm0-dump') items.push({ level: 'warning', kind: 'source', message: 'built from the M0 network dump, not a .cs1x: no terrain section, NetInfo parents derived from variant fields' });
  for (const r of city.report) if (r.level !== 'info') items.push({ level: 'warning', kind: 'source', message: `exporter ${r.level}${r.section ? ` (${r.section})` : ''}: ${r.message}` });

  const infos = new Map(city.netInfos.map(i => [i.name, i]));
  const nodes = new Map(city.nodes.map(n => [n.id, n]));
  const baseName = (name: string) => {
    const p = infos.get(name)?.parent;
    return p && infos.has(p) ? p : name;
  };

  // Vanilla roads: a road type, or one of its IMT / TM:PE variants, whose generated profile matches a vanilla CS2 road
  // uses that road (docs/DECISIONS.md #100). Not with an explicit "Road Builder" choice for the type.
  const preferVanilla = opts.preferVanilla !== false && !!opts.roads?.roads?.length;
  const vanillaAllowed = (row: { override?: MappingOverride }) => preferVanilla && row.override?.mode !== 'rb';
  const vanillaByVariant = new Map<string, VanillaMatch>();

  // ---- mapping rows (whole city)
  const rows = new Map<string, MappingRow>();
  const targetOf = new Map<string, ResolvedTarget>();
  const rowFor = (seg: Cs1Segment) => {
    const base = baseName(seg.info);
    let row = rows.get(base);
    if (!row) {
      const info = infos.get(base);
      const target: ResolvedTarget = info ? resolveTarget(info, overrides[base]) : { mode: 'skip', reason: 'NetInfo missing from netinfos.json', kind: 'unmapped-netinfo' };
      row = {
        netInfo: base, title: info ? netInfoTitle(info) : base, category: info ? categorize(info) : 'other', ai: info?.ai ?? seg.ai ?? '',
        infos: [], segments: 0, segmentsInCrop: 0, target, override: overrides[base], prefabKeys: [],
        heightMode: 'conform', heightModeAuto: 'conform',
      };
      row.heightModeAuto = defaultHeightMode(info, row.category);
      const hm = opts.heightModes?.[base] ?? opts.heightMode;
      row.heightMode = hm && HEIGHT_MODES.includes(hm) ? hm : row.heightModeAuto;
      if (info && target.mode === 'rb') row.rb = convert(info)?.report;
      if (row.rb && vanillaAllowed(row)) row.vanilla = matchVanilla(row.rb.profile, opts.roads);
      rows.set(base, row);
      targetOf.set(base, target);
    }
    if (!row.infos.includes(seg.info)) row.infos.push(seg.info);
    row.segments++;
    return row;
  };

  // ---- crop classification
  const inside: Cs1Segment[] = [];
  let outside = 0;
  const crossing: number[] = [];
  const nodeIn = (id: number) => { const n = nodes.get(id); return !!n && inCrop(crop, n.position[0], n.position[2]); };
  for (const s of city.segments) {
    const row = rowFor(s);
    const a = nodeIn(s.startNode), b = nodeIn(s.endNode);
    if (a && b) { inside.push(s); row.segmentsInCrop++; }
    else if (a || b) crossing.push(s.id);
    else outside++;
  }

  // ---- IMT coverage on crop road segments
  const isBuildingNet = (s: Cs1Segment) => /(^|,\s*)Untouchable(,|$)/.test(s.flags ?? '');
  const roadSegs = inside.filter(s => targetOf.get(baseName(s.info))?.mode === 'rb' && !isBuildingNet(s));
  const coverage = laneCoverage({ segments: roadSegs, netInfos: infos, imtFillers: city.imtFillers }, info => analyse(info).slots);
  const segOverrides = wholeLaneOverrides(coverage);
  /** Per plan segment, the CS1 lane positions its Road Builder variant turns into a median (IMT islands): not driving lanes. */
  const medianLanes = new Map<number, number[]>();
  // TM:PE speed limits and bus-only lanes become RB variants too.
  const lv = trafficLaneVariants(city, infos, roadSegs, opts.traffic !== false);
  items.push(...lv.items);
  const speedGroups = new Map<string, number>();
  let speedOnVanilla = 0;

  // ---- segments
  const prefabs: Record<string, PlanPrefab> = {};
  const keyById = new Map<string, string>();
  const rbConfigs = new Map<string, { config: RbConfig; report: RbReport; key: string; segments: number }>();
  const planSegments: PlanSegment[] = [];
  const usedNodes = new Set<number>();
  const skipped = new Map<string, { count: number; kind: PackageReportItem['kind']; reason: string; ids: number[] }>();
  const skip = (label: string, kind: PackageReportItem['kind'], reason: string, id: number) => {
    const k = `${kind}|${label}`;
    let e = skipped.get(k);
    if (!e) { e = { count: 0, kind, reason, ids: [] }; skipped.set(k, e); }
    e.count++;
    if (e.ids.length < 20) e.ids.push(id);
  };
  const rbKey = (id: string) => {
    let key = keyById.get(id);
    if (key) return key;
    key = `rb:${id.slice(1, 9)}`;
    if (prefabs[key]) key = `rb:${id.slice(1, 24)}`;
    keyById.set(id, key);
    return key;
  };
  const { dx, dz } = cropTransform(crop);
  const tx = (p: number[]): [number, number, number] => [r3(p[0] + dx), r3(p[1]), r3(p[2] + dz)];

  for (const s of inside) {
    const base = baseName(s.info);
    const row = rows.get(base)!;
    const target = targetOf.get(base)!;
    const baseInfo = infos.get(base);
    if (isBuildingNet(s)) { skip(base, 'not-placeable', 'part of a building (Untouchable sub-network), comes with buildings in M3', s.id); continue; }
    if (target.mode === 'skip' || !baseInfo) {
      const t = target.mode === 'skip' ? target : { kind: 'unmapped-netinfo' as const, reason: 'NetInfo missing' };
      skip(base, t.kind, t.reason, s.id);
      continue;
    }
    let key: string;
    if (target.mode === 'rb') {
      // Lane overrides are found on the segment's own NetInfo (IMT fills first, then TM:PE bus-only
      // lanes); keep the ones that match a lane of the ground NetInfo.
      const imt = segOverrides.get(s.id), tmBus = lv.bus.get(s.id);
      let ov: Map<number, LaneOverrideKind> | null = null;
      let label = 'IMT';
      if (imt || tmBus) {
        const raw = new Map<number, LaneOverrideKind>(imt ?? []);
        let fromTm = false;
        for (const [p, k] of tmBus ?? []) if (!raw.has(p)) { raw.set(p, k); fromTm = true; }
        label = imt && fromTm ? 'IMT+TM:PE' : imt ? 'IMT' : 'TM:PE';
        const baseSlots = analyse(baseInfo).slots;
        ov = new Map([...raw].filter(([p]) => baseSlots.some(sl => Math.abs(sl.pos - p) < 0.25)));
        const lost = [...raw].filter(([p]) => !ov!.has(p));
        if (lost.length) items.push({ level: 'warning', kind: imt ? 'imt-partial-fill' : 'traffic', message: `lane override(s) ${lost.map(([p, k]) => `${p}=${k}`).join(', ')} on ${s.info} have no matching lane in ${base}; ignored`, cs1: { segment: s.id } });
        if (!ov.size) ov = null;
      }
      const tmSpeed = lv.speed.get(s.id);
      const speed = tmSpeed != null && row.rb && Math.abs(tmSpeed - row.rb.speedKmh) >= 5 ? Math.round(tmSpeed / 5) * 5 : null;
      const medians = ov ? [...ov].filter(([, k]) => k !== 'bus').map(([p]) => p) : [];
      if (medians.length) medianLanes.set(s.id, medians);
      const laneSig = overrideSignature(ov);
      const sig = speed != null ? `${laneSig ? laneSig + ',' : ''}speed=${speed}` : laneSig;
      const ck = `${base}|${sig}`;
      let vm = vanillaByVariant.get(ck);
      if (vm === undefined && vanillaAllowed(row)) {
        const res = convert(baseInfo, ov, { speedKmh: speed, overrideLabel: label });
        vm = res ? matchVanilla(res.report.profile, opts.roads) : { road: null, reason: '' };
        vanillaByVariant.set(ck, vm);
      }
      if (vm?.road) {
        key = prefabKeyVanilla(vm.road.name);
        if (!prefabs[key]) prefabs[key] = { type: 'RoadPrefab', name: vm.road.name, source: base, matched: true, ...(laneSig ? { imt: laneSig, lanesFrom: label } : {}) };
        else if (!prefabs[key].source.split(', ').includes(base)) prefabs[key].source += `, ${base}`;
        if (ov) items.push({ level: 'info', kind: label === 'TM:PE' ? 'traffic' : 'imt-override', message: `${label} lane override ${laneSig} on ${base}: the result matches vanilla ${vm.road.name}`, cs1: { segment: s.id } });
      } else {
        let entry = rbConfigs.get(ck);
        if (!entry) {
          const res = convert(baseInfo, ov, { speedKmh: speed, overrideLabel: label });
          if (!res) { skip(base, 'unmapped-netinfo', 'road has no drive lane Road Builder can represent', s.id); continue; }
          entry = { ...res, key: rbKey(res.config.ID), segments: 0 };
          rbConfigs.set(ck, entry);
          prefabs[entry.key] = {
            type: 'RoadBuilderPrefab', name: res.config.ID, source: base,
            ...(laneSig ? { imt: laneSig, lanesFrom: label } : {}), ...(speed != null ? { speedKmh: speed } : {}),
          };
        }
        entry.segments++;
        key = entry.key;
        if (speed != null) speedGroups.set(`${base}|${speed}`, (speedGroups.get(`${base}|${speed}`) ?? 0) + 1);
        if (ov) items.push({ level: 'info', kind: label === 'TM:PE' ? 'traffic' : 'imt-override', message: `${label} lane override ${laneSig} on ${base}: segment uses RB variant ${entry.config.ID}`, cs1: { segment: s.id } });
      }
    } else {
      if (lv.speed.has(s.id)) speedOnVanilla++;
      key = prefabKeyVanilla(target.name);
      if (!prefabs[key]) prefabs[key] = { type: target.type, name: target.name, source: base };
      else if (prefabs[key].source !== base && !prefabs[key].source.split(', ').includes(base)) prefabs[key].source += `, ${base}`;
    }
    // height mode (#110–113): a non-default mode gets its own key, so each prefab has one mode
    const fk = foldHeightMode(key, row.heightMode, row.category);
    if (fk !== key) {
      if (!prefabs[fk]) prefabs[fk] = { ...prefabs[key], source: base };
      else if (!prefabs[fk].source.split(', ').includes(base)) prefabs[fk].source += `, ${base}`;
      key = fk;
    }
    prefabs[key].heightMode = row.heightMode;
    if (!row.prefabKeys.includes(key)) row.prefabKeys.push(key);

    const info = infos.get(s.info);
    const kind = segmentKind(s, info);
    const sn = nodes.get(s.startNode), en = nodes.get(s.endNode);
    let eStart = nodeElevation(kind, sn), eEnd = nodeElevation(kind, en);
    let start = s.startNode, end = s.endNode;
    let bez = s.bezier.map(tx) as Bezier;
    // An inverted CS1 segment has its lanes mirrored: orient the CS2 course end→start so the RB
    // config's left→right lane order and lane directions match what was built in CS1.
    const reversed = !!s.inverted;
    if (reversed) {
      [start, end] = [end, start];
      [eStart, eEnd] = [eEnd, eStart];
      bez = [bez[3], bez[2], bez[1], bez[0]];
    }
    usedNodes.add(start); usedNodes.add(end);
    const halfWidth = Number(info?.fields.m_halfWidth ?? baseInfo.fields.m_halfWidth ?? 0);
    planSegments.push({ id: s.id, prefab: key, start, end, bezier: bez, elevationStart: eStart, elevationEnd: eEnd, kind, ...(reversed ? { reversed } : {}), cs1Info: s.info, halfWidth: r2(halfWidth) });
  }
  // a key whose segments all moved to a folded key is left without segments: drop it
  { const used = new Set(planSegments.map(s => s.prefab)); for (const k of Object.keys(prefabs)) if (!used.has(k)) delete prefabs[k]; }

  const planNodes: PlanNode[] = [...usedNodes].sort((a, b) => a - b).map(id => {
    const n = nodes.get(id)!;
    return { id, x: r3(n.position[0] + dx), z: r3(n.position[2] + dz), y: r3(n.position[1]), heightAboveTerrain: r2(Number(n.heightAboveTerrain ?? 0)), elevation: Number(n.elevation ?? 0) };
  });

  // ---- M2 sections
  const ctx: PlanContext = {
    city, crop, dx, dz, infos, nodes, segments: new Map(city.segments.map(s => [s.id, s])),
    planSegments: new Set(planSegments.map(s => s.id)), planNodes: usedNodes, reversed: new Set(planSegments.filter(s => s.reversed).map(s => s.id)),
    inCrop: (x, z) => inCrop(crop, x, z),
    medianLanes,
    t: translator(opts.lang ?? 'en'), lang: opts.lang ?? 'en',
  };
  // Node Controller shifts (#107): segment ends off the node centre, as in CS1 (m2/ncalign.ts, worker-5)
  const ncAlign = alignNcShifts(planSegments, ctx);
  items.push(...ncAlign.items);
  const zoning = buildZoning(ctx, opts.zoning ?? DEFAULT_ZONING);
  const water = buildWater(ctx, opts.water !== false);
  const transit = buildTransit(ctx, opts.transit !== false);
  const traffic = buildTraffic(ctx, opts.traffic !== false);
  const nc = buildNodeController(ctx, ncAlign);
  const buildings = buildBuildings(ctx, { enabled: opts.buildings !== false, catalogue: opts.catalogue ?? null, overrides: opts.assetOverrides, fallbacks: opts.assetFallbacks, fallback: opts.assetFallback, utilitiesNearby: opts.utilitiesNearby, theme: (opts.zoning ?? DEFAULT_ZONING).theme });
  // #128: utility facilities in the crop, for the Overview notice and the report
  const utilityMatches = buildings.matches.filter(m => m.utility && m.decision === 'place' && m.countInCrop > 0);
  const utilityFacilities = utilityMatches.length ? {
    types: utilityMatches.map(m => m.title), buildings: utilityMatches.reduce((t, m) => t + m.countInCrop, 0),
    moving: utilityMatches.filter(m => m.fallback !== 'skip').reduce((t, m) => t + m.countInCrop, 0), optedIn: opts.utilitiesNearby === true,
  } : null;
  if (utilityFacilities) items.push(utilityFacilities.optedIn
    ? { level: 'info', kind: 'buildings', message: `${utilityFacilities.buildings} power, water and sewage building(s): ${utilityFacilities.moving} may move up to 400 m if they don't fit at their CS1 spot (utilities opt-in, #128)`, count: utilityFacilities.buildings }
    : { level: 'warning', kind: 'buildings', message: `${utilityFacilities.buildings} power, water and sewage building(s) (${utilityFacilities.types.join(', ')}) stay at their CS1 spot; CS2's are often bigger, and one that doesn't fit is listed for you to place. Without them the city has no power or water: the web tool's "Let them move nearby" (CLI --utilities-nearby) lets them move up to 400 m`, count: utilityFacilities.buildings });
  const districts = buildDistricts(ctx, opts.districts !== false);
  // CS2 lines get vehicles only from a depot of their type. Bus and tram: a default depot in the city
  // (buildings/lib/plan.mjs). Metro and train: never inside the city (docs/DECISIONS.md #83); the importer puts a yard
  // at the crop edge where the track leaves it, or the user places one (transit.depotMode).
  const isRail = (t: string) => (RAIL_DEPOT_TYPES as readonly string[]).includes(t);
  if (buildings.section && transit.section) {
    const roadLines = { lines: transit.section.lines.filter(l => !isRail(l.cs1Type)) };
    const added = addDefaultDepots(buildings.section, roadLines, opts.catalogue ?? undefined) as { type: string; prefab: string; lines: string[] }[];
    for (const d of added) {
      buildings.items.push({
        level: 'info', kind: 'buildings',
        message: `default ${d.prefab} added for ${d.lines.join(', ')}: CS1 had no ${d.type.toLowerCase()} depot and CS2 lines need one`,
      });
    }
    if (added.length) buildings.summary.details.splice(-1, 0, ctx.t('sum.buildings.depots', { list: added.map(d => d.prefab).join(', ') }));
  }
  if (transit.section) {
    const railTypes = RAIL_DEPOT_TYPES.filter(t => transit.section!.lines.some(l => l.cs1Type === t));
    if (railTypes.length) {
      transit.section.depotMode = Object.fromEntries(railTypes.map(t => [t, opts.depotMode?.[t] === 'manual' ? 'manual' : 'edge']));
      for (const t of railTypes) {
        const names = transit.section.lines.filter(l => l.cs1Type === t).map(l => l.name);
        const mode = transit.section.depotMode[t];
        transit.items.push({
          level: 'info', kind: 'transit',
          message: mode === 'edge'
            ? `${t} lines (${names.join(', ')}): the importer places a ${t === 'Metro' ? 'subway' : 'rail'} yard at the crop edge where their track leaves the area; a line whose track stays inside needs one placed by you`
            : `${t} lines (${names.join(', ')}): place a ${t === 'Metro' ? 'subway' : 'rail'} yard yourself (you chose so)`,
        });
        transit.summary.details.push(ctx.t(mode === 'edge' ? 'sum.transit.depotEdge' : 'sum.transit.depotManual', { type: ctx.t(t === 'Metro' ? 'sum.transit.metro' : 'sum.transit.train') }));
      }
    }
  }
  const objects = buildObjects(ctx, { enabled: opts.objects !== false, catalogue: opts.catalogue ?? null, theme: (opts.zoning ?? DEFAULT_ZONING).theme });
  const surfaces = buildSurfaces(ctx, opts.surfaces !== false);
  const utilities = opts.utilities === false ? { section: null, items: [] } : buildUtilities({
    segments: city.segments, nodes, infos, inCrop: (x, z) => inCrop(crop, x, z), dx, dz, planSegments, prefabs, segmentKind, nodeElevation,
  });
  for (const r of [zoning, water, transit, traffic, nc, buildings, districts, objects, surfaces, utilities]) items.push(...r.items);
  if (city.manifest.traffic?.leftHand === true) items.push({ level: 'info', kind: 'traffic', message: 'the CS1 city drives on the left (plan.source.leftHandTraffic): its lanes are mirrored, so lane connections and lane arrows refer to mirrored lanes' });
  for (const [k, n] of speedGroups) {
    const [base, kmh] = k.split('|');
    items.push({ level: 'info', kind: 'speed-limit', message: `${base}: ${n} segment(s) with a TM:PE speed limit of ${kmh} km/h use a Road Builder variant with that speed`, count: n, cs1: { netInfo: base } });
  }
  if (speedOnVanilla) items.push({ level: 'info', kind: 'speed-limit', message: `${speedOnVanilla} track or path segment(s) with a TM:PE speed limit keep the CS2 default speed`, count: speedOnVanilla });

  // ---- report
  if (outside) items.push({ level: 'info', kind: 'outside-crop', message: `${outside} segments lie outside the crop`, count: outside });
  if (crossing.length) items.push({ level: 'info', kind: 'crossing-crop-edge', message: `${crossing.length} segments cross the crop edge (one node outside) and are left out`, count: crossing.length, cs1: { segments: crossing.slice(0, 50) } });
  for (const [k, e] of skipped) {
    const label = k.slice(k.indexOf('|') + 1);
    items.push({ level: e.kind === 'unmapped-netinfo' ? 'warning' : 'info', kind: e.kind, message: `${label}: ${e.count} segment(s) skipped – ${e.reason}`, count: e.count, cs1: { netInfo: label, segments: e.ids } });
  }
  for (const [base, o] of Object.entries(overrides)) {
    if (o.mode !== 'auto' && rows.has(base)) items.push({ level: 'info', kind: 'mapping-override', message: `${base}: mapping overridden → ${o.mode === 'vanilla' ? `${o.type} "${o.name}"` : o.mode}` });
  }
  for (const e of rbConfigs.values()) {
    const loss = Math.abs(e.report.diff) >= 0.5;
    if (!loss && !e.report.notes.length) continue;
    items.push({
      level: loss ? 'warning' : 'info', kind: loss ? 'width-loss' : 'lane-approximation',
      message: `${e.report.title}${e.report.imtOverrides ? ` (${e.report.imtOverrides})` : ''}: CS1 ${e.report.cs1Width} m → RB ${e.report.rbWidth} m${e.report.notes.length ? '; ' + e.report.notes.join('; ') : ''}`,
      cs1: { netInfo: e.report.cs1 }, count: e.segments,
    });
  }
  // IMT fills in the crop that do not become lanes.
  const segIds = new Set(inside.map(s => s.id));
  const wholeFillers = new Set(coverage.filter(c => c.lanes.some(l => l.whole) && c.fillerKind !== 'paint').map(c => c.fillerIdx));
  city.imtFillers.forEach((f, idx) => {
    const kind = classifyFiller(f);
    const id = f.fillerId ?? idx;
    const color = f.color ? `rgba(${f.color.join(',')})` : '';
    if (f.markingType === 'Node') {
      const n = nodes.get(f.markingId);
      if (!n || !inCrop(crop, n.position[0], n.position[2])) return;
      if (surfaces.section && f.styleType && SURFACE_MAP[f.styleType]) return; // a CS2 surface (plan.surfaces)
      items.push({ level: 'info', kind: 'imt-node-fill', message: `IMT node fill ${f.styleType} ${color} on node ${f.markingId} (${kind}): no CS2 equivalent, not converted`, cs1: { node: f.markingId, filler: id } });
    } else if (f.markingType === 'Segment' && segIds.has(f.markingId)) {
      if (kind === 'paint') items.push({ level: 'info', kind: 'imt-paint', message: `IMT segment paint ${f.styleType} ${color} on segment ${f.markingId}: paint only, not converted`, cs1: { segment: f.markingId, filler: id } });
      else if (!wholeFillers.has(idx)) items.push({ level: 'warning', kind: 'imt-partial-fill', message: `IMT ${kind} fill on segment ${f.markingId} does not cover a whole lane: road left plain`, cs1: { segment: f.markingId, filler: id } });
    }
  });

  const byPrefabType: Record<string, number> = {};
  for (const s of planSegments) { const t = prefabs[s.prefab].type; byPrefabType[t] = (byPrefabType[t] ?? 0) + 1; }
  const skippedCount = crossing.length + [...skipped.values()].reduce((t, e) => t + e.count, 0);
  const b = cropBounds(crop);
  const plan: Plan = {
    format: 'cs1conv-plan', formatVersion: 1,
    generator: { ...GENERATOR, generatedAt: opts.generatedAt ?? new Date().toISOString() },
    source: {
      city: city.manifest.city?.name ?? 'unknown', saveName: city.manifest.city?.saveName ?? 'unknown', exportedAt: city.manifest.exportedAt ?? '',
      cs1xSha256: city.sha256, kind: city.source,
      ...(typeof city.manifest.traffic?.leftHand === 'boolean' ? { leftHandTraffic: city.manifest.traffic.leftHand } : {}),
    },
    crop: { ...crop },
    transform: { dx, dz, heightMode: 'terrain-relative' },
    bounds: { minX: b.minX + dx, maxX: b.maxX + dx, minZ: b.minZ + dz, maxZ: b.maxZ + dz },
    prefabs,
    nodes: planNodes,
    segments: planSegments,
    stats: {
      segments: planSegments.length, skipped: skippedCount, nodes: planNodes.length, outsideCrop: outside, crossingCropEdge: crossing.length,
      unmapped: [...skipped.values()].reduce((t, e) => t + e.count, 0), rbConfigs: rbConfigs.size, byPrefabType,
    },
    requires: [...new Set([...(rbConfigs.size ? ['RoadBuilder'] : []), ...(traffic.section?.requires ?? [])])],
    ...(zoning.section ? { zoning: zoning.section } : {}),
    ...(buildings.section ? { buildings: buildings.section } : {}),
    ...(districts.section ? { districts: districts.section } : {}),
    ...(objects.section ? { objects: objects.section } : {}),
    ...(surfaces.section ? { surfaces: surfaces.section } : {}),
    ...(water.section ? { water: water.section } : {}),
    ...(transit.section ? { transit: transit.section } : {}),
    ...(traffic.section ? { traffic: traffic.section } : {}),
    ...(utilities.section ? { utilities: utilities.section } : {}),
  };
  // Water as the scripted flow applies it (mapprep water.set), outside connections (mapprep connections).
  if (plan.water && city.water) {
    const w = convertWater(city.water, plan);
    const d = city.water.depth, g = city.water.depthGrid;
    if (d && g && city.terrain && city.terrain.resolution === d.resolution && w.seaLevel != null) {
      const rs = regionSources(d, g, cs1Heights(city.terrain), plan, w.seaLevel, w.sources);
      w.sources.push(...rs.sources);
      if (rs.sources.length) items.push({ level: 'info', kind: 'water', message: `${rs.sources.length} extra water source(s) keep CS1 rivers and lakes above sea level filled (${rs.regions.map(r => r.kind).join(', ')})`, count: rs.sources.length });
    }
    plan.water.apply = { seaLevel: w.seaLevel, sources: w.sources };
    if (d && g) plan.water.checkpoints = depthCheckpoints(d, g, plan);
  }
  if (opts.outsideConnections !== false && plan.segments.length) {
    const oc = deriveConnections(city, plan);
    if (oc.connections.length || oc.skipped.length) {
      plan.outsideConnections = oc;
      items.push({ level: 'info', kind: 'outside-connections', message: `${oc.counts.connections} outside connection(s) where the network leaves the crop (${oc.counts.road} road, ${oc.counts.train} train)${oc.skipped.length ? `; ${oc.skipped.length} skipped (end node not imported)` : ''}`, count: oc.counts.connections });
    }
  }
  const matched = Object.values(prefabs).filter(p => p.matched);
  if (matched.length) items.push({ level: 'info', kind: 'mapping-override', message: `${matched.length} road type(s) use a matching vanilla CS2 road instead of a Road Builder road: ${matched.map(p => `${p.source}${p.imt ? ` (${p.imt})` : ''} → ${p.name}`).join('; ')}`, count: matched.length });
  const order: Record<string, number> = { warning: 0, info: 1 };
  items.sort((x, y) => order[x.level] - order[y.level]);
  const pkg: BuiltPackage = {
    plan,
    lang: opts.lang ?? 'en',
    report: { format: 'cs1conv-report', formatVersion: 1, items },
    rbConfigs: [...rbConfigs.values()].map(e => e.config),
    rbVariants: [...rbConfigs.values()].map(e => ({ ...e.report, key: e.key, segments: e.segments })),
    mapping: [...rows.values()].sort((a, b) => Number(a.target.mode === 'skip') - Number(b.target.mode === 'skip') || b.segmentsInCrop - a.segmentsInCrop || b.segments - a.segments),
    errors: cv.errors,
    summary: [],
    extraSummary: [zoning.summary, water.summary, transit.summary, traffic.summary, nc.summary, buildings.summary, districts.summary, objects.summary],
    assetMatches: buildings.matches,
    roadConform: opts.roadConform !== false,
    utilityFacilities,
  };
  pkg.summary = conversionSummary(city, pkg);
  return pkg;
}

/** Default crop: the square of the given size holding the most road/highway segments. */
export function autoCrop(city: Cs1City, size: number): Crop & { roadSegments: number } {
  const infos = new Map(city.netInfos.map(i => [i.name, i]));
  const nodes = new Map(city.nodes.map(n => [n.id, n]));
  const isRoad = new Map<string, boolean>();
  const segs: [number, number, number, number][] = [];
  for (const s of city.segments) {
    let road = isRoad.get(s.info);
    if (road === undefined) {
      const i = infos.get(s.info);
      const base = (i?.parent && infos.get(i.parent)) || i;
      const cat = base ? categorize(base) : 'other';
      road = cat === 'road' || cat === 'highway';
      isRoad.set(s.info, road);
    }
    const a = nodes.get(s.startNode), b = nodes.get(s.endNode);
    if (road && a && b && !/Untouchable/.test(s.flags ?? '')) segs.push([a.position[0], a.position[2], b.position[0], b.position[2]]);
  }
  const c = bestCropCenter(segs, size, 250, city.manifest.mapSize);
  return { centerX: c.x, centerZ: c.z, size, roadSegments: c.count };
}

const SCALAR = String.raw`(?:-?\d[\d.eE+-]*|"(?:[^"\\\n]|\\.)*"|true|false|null)`;
const FLAT_ARRAY = new RegExp(String.raw`\[\s*(${SCALAR}(?:,\s*${SCALAR})*)\s*\]`, 'g');

/** JSON indented by one space, with arrays of scalars kept on one line (zone cells, beziers, ids). */
export function compactJson(v: unknown): string {
  return JSON.stringify(v, null, 1).replace(FLAT_ARRAY, (_m, inner: string) => `[${inner.match(new RegExp(SCALAR, 'g'))!.join(',')}]`);
}

/**
 * Writes the expensive parts of the package: the terrain files (4096² heightmap + world map + terrain.json) and the
 * plan.terrain pointer. Call it once before writing / zipping a package (CLI, download), not on every crop change.
 * Returns the extra files; pkg.plan.terrain and the terrain summary are updated in place.
 */
export function completePackage(city: Cs1City, pkg: BuiltPackage, opts: { terrain?: boolean; world?: boolean; conform?: boolean } = {}): { path: string; data: Uint8Array }[] {
  if (opts.terrain === false || !city.terrain) return [];
  const t = terrainFiles(city, pkg.plan, { world: opts.world, conform: opts.conform ?? pkg.roadConform });
  if (!t) return [];
  pkg.plan.terrain = t.terrain;
  const c = t.conform;
  if (c) {
    pkg.report.items.push({ level: 'info', kind: 'terrain', message: `road-conformed terrain: ${c.segments} ground road segment(s) and ${c.nodes} junction(s) burned into the heightmap (${c.pixelsChanged} pixels, up to +${c.maxRaise} / −${c.maxCut} m)${t.waterSourcesAdjusted ? `; ${t.waterSourcesAdjusted} water source(s) adjusted to keep their level` : ''}`, count: c.segments });
    if (c.skipped) pkg.report.items.push({ level: 'warning', kind: 'terrain', message: `${c.skipped} ground segment(s) more than ${c.maxDifference} m from the natural terrain were not burned in (segments ${(c.skippedIds ?? []).join(', ')})`, count: c.skipped, cs1: { segments: c.skippedIds ?? [] } });
  }
  const s = pkg.summary.find(c => c.id === 'terrain');
  if (s) {
    s.summary = terrainSummary(pkg.plan, pkg.lang);
    if (t.terrain.roadConformed) s.details = [...(s.details ?? []).filter(d => d !== translator(pkg.lang)('sum.terrain.conformed')), translator(pkg.lang)('sum.terrain.conformed')];
  }
  return t.files;
}

/** One line for the "What converts" list: heightmap and the sea level the mod will set. */
export function terrainSummary(plan: Plan, lang: Lang = 'en'): string {
  const tr = translator(lang);
  const sea = plan.water?.apply?.seaLevel ?? plan.water?.seaLevel;
  const t = plan.terrain;
  const range = t ? tr('sum.terrain.range', { min: Math.round(t.min), max: Math.round(t.max) }) : '';
  const seaText = sea != null ? tr('sum.terrain.seaCs1', { sea: fmtNumber(lang, Math.round(sea * 10) / 10, 1) }) : t ? tr('sum.terrain.seaDry', { sea: t.seaLevelDry }) : tr('sum.terrain.seaFromCs1');
  return tr('sum.terrain', { range, sea: seaText });
}

/** Files of the package, relative paths → text (UTF-8, no BOM). Binary terrain files come from completePackage(). */
export function packageFiles(pkg: BuiltPackage): { path: string; text: string }[] {
  return [
    { path: 'cs1conv-plan.json', text: compactJson(pkg.plan) + '\n' },
    { path: 'report.json', text: JSON.stringify(pkg.report, null, 2) + '\n' },
    ...pkg.rbConfigs.map(c => ({ path: `roadbuilder/${c.ID}.json`, text: JSON.stringify(c, null, 4) + '\n' })),
    { path: 'README.txt', text: packageReadme(pkg, pkg.summary) },
  ];
}

/** Mapping summary: how many used ground NetInfos go to RB / vanilla / skipped (whole city and crop). */
export function mappingStats(rows: MappingRow[]) {
  const count = (pred: (r: MappingRow) => boolean) => ({ all: rows.filter(pred).length, inCrop: rows.filter(r => pred(r) && r.segmentsInCrop > 0).length });
  return {
    netInfos: count(() => true),
    rb: count(r => r.target.mode === 'rb'),
    vanilla: count(r => r.target.mode === 'vanilla'),
    skippedNotPlaceable: count(r => r.target.mode === 'skip' && r.target.kind === 'not-placeable'),
    skippedUnmapped: count(r => r.target.mode === 'skip' && r.target.kind === 'unmapped-netinfo'),
  };
}
