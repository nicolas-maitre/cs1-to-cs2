// Build the import package (schema/README.md §2): cs1conv-plan.json + roadbuilder/*.json + report.json.

import { bestCropCenter, cropBounds, cropTransform, inCrop, validateCrop } from './crop.ts';
import { classifyFiller, laneCoverage, overrideSignature, wholeLaneOverrides } from './imt.ts';
import type { LaneOverrideKind } from './imt.ts';
import { categorize, prefabKeyVanilla, resolveTarget } from './mapping.ts';
import type { MappingOverride, NetCategory, ResolvedTarget } from './mapping.ts';
import { analyse, convert, netInfoTitle } from './rb.ts';
import type { RbConfig, RbReport } from './rb.ts';
import type {
  Bezier, Crop, Cs1City, Cs1Node, Cs1Segment, NetInfo, PackageReport, PackageReportItem, Plan, PlanNode, PlanPrefab, PlanSegment, SegmentKind,
} from './types.ts';

export const GENERATOR = { name: 'cs1conv-web', version: '0.1.0' };

export interface BuildOptions {
  crop: Crop;
  /** Per ground-NetInfo mapping overrides (key: NetInfo name). */
  overrides?: Record<string, MappingOverride>;
  generatedAt?: string;
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
}

export interface RbVariant extends RbReport { key: string; segments: number }

export interface BuiltPackage {
  plan: Plan;
  report: PackageReport;
  rbConfigs: RbConfig[];
  rbVariants: RbVariant[];
  mapping: MappingRow[];
  errors: string[];
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
      };
      if (info && target.mode === 'rb') row.rb = convert(info)?.report;
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
      // IMT overrides are found on the segment's own NetInfo; keep the ones that match a base lane.
      const raw = segOverrides.get(s.id);
      let ov: Map<number, LaneOverrideKind> | null = null;
      if (raw) {
        const baseSlots = analyse(baseInfo).slots;
        ov = new Map([...raw].filter(([p]) => baseSlots.some(sl => Math.abs(sl.pos - p) < 0.25)));
        const lost = [...raw].filter(([p]) => !ov!.has(p));
        if (lost.length) items.push({ level: 'warning', kind: 'imt-partial-fill', message: `IMT lane override(s) ${lost.map(([p, k]) => `${p}=${k}`).join(', ')} on ${s.info} have no matching lane in ${base}; ignored`, cs1: { segment: s.id } });
        if (!ov.size) ov = null;
      }
      const sig = overrideSignature(ov);
      const ck = `${base}|${sig}`;
      let entry = rbConfigs.get(ck);
      if (!entry) {
        const res = convert(baseInfo, ov);
        if (!res) { skip(base, 'unmapped-netinfo', 'road has no drive lane Road Builder can represent', s.id); continue; }
        entry = { ...res, key: rbKey(res.config.ID), segments: 0 };
        rbConfigs.set(ck, entry);
        prefabs[entry.key] = { type: 'RoadBuilderPrefab', name: res.config.ID, source: base, ...(sig ? { imt: sig } : {}) };
      }
      entry.segments++;
      key = entry.key;
      if (ov) items.push({ level: 'info', kind: 'imt-override', message: `IMT fill → ${sig} on ${base}: segment uses RB variant ${entry.config.ID}`, cs1: { segment: s.id } });
    } else {
      key = prefabKeyVanilla(target.name);
      if (!prefabs[key]) prefabs[key] = { type: target.type, name: target.name, source: base };
      else if (prefabs[key].source !== base && !prefabs[key].source.split(', ').includes(base)) prefabs[key].source += `, ${base}`;
    }
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
    planSegments.push({ id: s.id, prefab: key, start, end, bezier: bez, elevationStart: eStart, elevationEnd: eEnd, kind, ...(reversed ? { reversed } : {}), cs1Info: s.info });
  }

  const planNodes: PlanNode[] = [...usedNodes].sort((a, b) => a - b).map(id => {
    const n = nodes.get(id)!;
    return { id, x: r3(n.position[0] + dx), z: r3(n.position[2] + dz), y: r3(n.position[1]), heightAboveTerrain: r2(Number(n.heightAboveTerrain ?? 0)), elevation: Number(n.elevation ?? 0) };
  });

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
      message: `${e.report.title}${e.report.imtOverrides ? ` (IMT ${e.report.imtOverrides})` : ''}: CS1 ${e.report.cs1Width} m → RB ${e.report.rbWidth} m${e.report.notes.length ? '; ' + e.report.notes.join('; ') : ''}`,
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
  };
  const order: Record<string, number> = { warning: 0, info: 1 };
  items.sort((x, y) => order[x.level] - order[y.level]);
  return {
    plan,
    report: { format: 'cs1conv-report', formatVersion: 1, items },
    rbConfigs: [...rbConfigs.values()].map(e => e.config),
    rbVariants: [...rbConfigs.values()].map(e => ({ ...e.report, key: e.key, segments: e.segments })),
    mapping: [...rows.values()].sort((a, b) => Number(a.target.mode === 'skip') - Number(b.target.mode === 'skip') || b.segmentsInCrop - a.segmentsInCrop || b.segments - a.segments),
    errors: cv.errors,
  };
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

/** Files of the package, relative paths → text (UTF-8, no BOM). */
export function packageFiles(pkg: BuiltPackage): { path: string; text: string }[] {
  return [
    { path: 'cs1conv-plan.json', text: JSON.stringify(pkg.plan, null, 1) + '\n' },
    { path: 'report.json', text: JSON.stringify(pkg.report, null, 2) + '\n' },
    ...pkg.rbConfigs.map(c => ({ path: `roadbuilder/${c.ID}.json`, text: JSON.stringify(c, null, 4) + '\n' })),
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
