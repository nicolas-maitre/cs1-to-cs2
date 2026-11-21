// What the M2 mappers get from buildPackage: the city, the crop and what the road plan contains.

import type { PackageReportItem } from '../types.ts';
import type { ConvCategory } from '../summary.ts';
import type { Crop, Cs1City, Cs1Node, Cs1Segment, NetInfo } from '../types.ts';
import type { Lang, T } from '../i18n/index.ts';

export interface PlanContext {
  city: Cs1City;
  crop: Crop;
  dx: number;
  dz: number;
  infos: Map<string, NetInfo>;
  nodes: Map<number, Cs1Node>;
  segments: Map<number, Cs1Segment>;
  /** CS1 segment ids in the plan. */
  planSegments: Set<number>;
  /** CS1 node ids in the plan. */
  planNodes: Set<number>;
  /** Segments whose course was reversed (CS1 Invert flag). */
  reversed: Set<number>;
  inCrop: (x: number, z: number) => boolean;
  /** Per segment, CS1 lane positions its Road Builder variant turns into a median (IMT islands); absent = none. */
  medianLanes?: Map<number, number[]>;
  /** Translator for the "What converts" summaries (report items stay in English). */
  t: T;
  lang: Lang;
}

export interface SectionResult<T> {
  section?: T;
  items: PackageReportItem[];
  summary: ConvCategory;
}

export const r1 = (v: number) => Math.round(v * 10) / 10;
export const r2 = (v: number) => Math.round(v * 100) / 100;
export const r3 = (v: number) => Math.round(v * 1000) / 1000;
export const plural = (n: number, w: string, pl = w + 's') => `${n.toLocaleString('en')} ${n === 1 ? w : pl}`;

/** Count occurrences into a Map. */
export function tally<K>(m: Map<K, number>, k: K, n = 1) { m.set(k, (m.get(k) ?? 0) + n); }

/** Vehicle (driving) lanes of a NetInfo in NetInfo lane order, with their position. */
export interface DriveLane { index: number; position: number; width: number; direction: string; vehicleType: string; laneType: string }

/**
 * Bicycle-only lanes (laneType Vehicle, vehicleType Bicycle) are not driving lanes: the generated Road Builder roads
 * put bikes on the sidewalk or in a bike lane of their own, so counting them would shift every lane's ordinal and
 * count against the CS2 road (worker-3: Cacao, 212 of 764 lane references didn't resolve).
 */
const bicycleOnly = (vehicleType: string) => vehicleType.split(/[,|]\s*/).every(v => v.trim() === 'Bicycle');
const isDrive = (laneType: string, vehicleType: string) => /Vehicle|TransportVehicle/.test(laneType) && !/Parking/.test(laneType) && vehicleType !== 'None' && vehicleType !== '' && !bicycleOnly(vehicleType);

export function driveLanes(info: NetInfo | undefined): DriveLane[] {
  if (!info) return [];
  return info.lanes
    .map((l, index) => ({ index, position: Number(l.fields.m_position) || 0, width: Number(l.fields.m_width) || 0, direction: String(l.fields.m_direction ?? ''), vehicleType: String(l.fields.m_vehicleType ?? ''), laneType: String(l.fields.m_laneType ?? '') }))
    .filter(l => isDrive(l.laneType, l.vehicleType));
}

/**
 * A lane in the plan's frame. The plan builds every course start → end and reverses inverted CS1
 * segments, so in the plan frame a lane keeps its NetInfo position (positive = right of start → end)
 * and its NetInfo direction (Forward = along start → end).
 */
export interface PlanLaneRef {
  segment: number;
  /** 0-based index among the segment's driving lanes, left → right in the plan frame. */
  ordinal: number;
  /** Number of driving lanes on the segment. */
  count: number;
  /** CS1 lane centre, metres from the road centre line (+ = right of start → end). */
  offset: number;
  dir: 'forward' | 'backward' | 'both';
  /** CS1 NetInfo lane index (traceability). */
  cs1LaneIndex: number;
}

/**
 * Driving lanes of a plan segment as its CS2 road has them: the NetInfo's driving lanes without the ones its Road
 * Builder variant turned into a median (an IMT island over the whole lane), so ordinals and counts match the CS2 road.
 */
export function segmentDriveLanes(ctx: PlanContext, segmentId: number): DriveLane[] {
  const seg = ctx.segments.get(segmentId);
  const medians = ctx.medianLanes?.get(segmentId) ?? [];
  return driveLanes(seg ? ctx.infos.get(seg.info) : undefined).filter(l => !medians.some(p => Math.abs(p - l.position) < 0.25));
}

/** The CS1 lane is a driving lane that the segment's Road Builder variant replaced with a median (IMT island). */
export function laneRemovedByMedian(ctx: PlanContext, segmentId: number, laneIndex: number): boolean {
  const medians = ctx.medianLanes?.get(segmentId);
  if (!medians?.length) return false;
  const seg = ctx.segments.get(segmentId);
  const lane = driveLanes(seg ? ctx.infos.get(seg.info) : undefined).find(l => l.index === laneIndex);
  return !!lane && medians.some(p => Math.abs(p - lane.position) < 0.25);
}

export function planLaneRef(ctx: PlanContext, segmentId: number, laneIndex: number): PlanLaneRef | null {
  const lanes = segmentDriveLanes(ctx, segmentId).sort((a, b) => a.position - b.position);
  const k = lanes.findIndex(l => l.index === laneIndex);
  if (k < 0) return null;
  const l = lanes[k];
  const dir = /Both|AvoidBoth/.test(l.direction) ? 'both' : /Backward/.test(l.direction) ? 'backward' : 'forward';
  return { segment: segmentId, ordinal: k, count: lanes.length, offset: r2(l.position), dir, cs1LaneIndex: laneIndex };
}

/**
 * Driving lanes of a segment whose traffic flows into the given node (CS1 frame: Forward lanes flow
 * start → end, mirrored on inverted segments).
 */
export function inboundLanes(ctx: PlanContext, segmentId: number, nodeId: number): DriveLane[] {
  const seg = ctx.segments.get(segmentId);
  if (!seg) return [];
  const intoEnd = seg.endNode === nodeId;
  return segmentDriveLanes(ctx, segmentId).filter(l => {
    if (/Both/.test(l.direction)) return true;
    const forward = /Forward/.test(l.direction) !== !!seg.inverted;
    return forward === intoEnd;
  });
}
