// CS1 road NetInfo lane layout → CS2 Road Builder RoadConfig. Port of
// spikes/m0-roadbuilder-configs/wq-to-rb.mjs (analyse / applyOverrides / convert), same output
// for every road the spike handled, so the ids and files of the deployed M0 configs are unchanged.
//
// Additions over the spike (web/DECISIONS-web.md):
//  - highways (CS1 m_highwayRules, no pedestrian lanes) get RB Category "Highway" and 2 m
//    asphalt shoulders instead of sidewalks, highway addons and no zoning;
//  - a road with no drive lane returns null (→ unmapped, reported).
//
// Option names/values come from Road Builder 0.7/0.8 source (NOTES-roadbuilder.md). Only exact,
// existing section combinations are emitted (RB 0.7.0 has no fuzzy fallback).

import { sha1Hex } from './hash.ts';
import { overrideSignature } from './imt.ts';
import type { LaneOverrideKind } from './imt.ts';
import type { NetInfo } from './types.ts';

const RB = 'RoadBuilder.LaneGroups.';
const SIDEWALK_WIDTHS: Record<string, number[]> = {
  '': [1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5, 5, 5.5, 6, 7],
  P: [5, 5.5, 6, 7],
  B: [3.5, 4.5, 5, 5.5, 6, 7],
};
const MEDIAN_WIDTHS = [1, 2, 5];
const SHOULDER_WIDTHS = [1, 2];

export interface RbLane {
  Version: 5;
  SectionPrefabName: string | null;
  GroupPrefabName: string;
  GroupOptions: Record<string, string>;
  Invert: boolean;
}

export interface RbConfig {
  Type: 'RoadConfig';
  Version: 5;
  OriginalID: string;
  ID: string;
  Name: string;
  PillarPrefabName: string;
  SpeedLimit: number;
  MaxSlopeSteepness: number;
  Category: 'Road' | 'Highway';
  Addons: string;
  Lanes: RbLane[];
  ToolbarState: 'Inherit';
}

export type SlotKind = 'car' | 'bus' | 'bus-on-parking' | 'tram' | 'parking' | 'bike' | 'trolleybus' | 'monorail' | 'median' | 'other';

interface Part { pos: number; width: number; laneType: string; vehicleType: string; direction: string; speed: number }

export interface Slot {
  pos: number;
  width: number;
  parts: Part[];
  kind: SlotKind;
  tram: boolean;
  types: string;
  invert: boolean;
  speedKmh: number;
  imt?: LaneOverrideKind;
  grass?: boolean;
  absorbedParking?: number;
}

interface DecorLane { pos: number; trees: string[] }

export interface Analysis {
  halfWidth: number;
  pavement: number;
  slots: Slot[];
  decor: DecorLane[];
  pedestrianLanes: number;
  highway: boolean;
}

export interface RbReport {
  cs1: string;
  imtOverrides?: string;
  title: string;
  id: string;
  category: 'Road' | 'Highway';
  cs1Width: number;
  rbWidth: number;
  diff: number;
  speedKmh: number;
  notes: string[];
  cs1Slots: string[];
  rbLanes: string[];
}

const nearest = (v: number, list: number[]) => list.reduce((a, b) => (Math.abs(b - v) < Math.abs(a - v) ? b : a));
const fmtM = (v: number) => `${v}m`;
const str = (v: unknown) => (v == null ? '' : String(v));

/** Normalise a CS1 NetInfo into a left→right cross-section model. */
export function analyse(info: NetInfo, overrides: Map<number, LaneOverrideKind> | null = null): Analysis {
  const f = info.fields;
  const halfWidth = Number(f.m_halfWidth) || 0, pavement = Number(f.m_pavementWidth) || 0;
  const lanes = info.lanes.map(l => ({
    pos: Number(l.fields.m_position) || 0, width: Number(l.fields.m_width) || 0,
    laneType: str(l.fields.m_laneType), vehicleType: str(l.fields.m_vehicleType),
    direction: str(l.fields.m_direction), speed: Number(l.fields.m_speedLimit) || 0,
    trees: (l.props ?? []).filter(p => p.tree || p.finalTree).map(p => (p.tree ?? p.finalTree) as string),
  }));
  const drive = lanes
    .filter(l => /Vehicle|TransportVehicle|Parking/.test(l.laneType) && l.vehicleType !== 'None')
    .filter(l => Math.abs(l.pos) < halfWidth - pavement + 0.01);
  // Merge lanes sharing a position (e.g. car + tram rails on the same lane).
  const slots: Slot[] = [];
  for (const l of [...drive].sort((a, b) => a.pos - b.pos)) {
    const s = slots.find(s => Math.abs(s.pos - l.pos) < 0.2);
    if (s) { s.parts.push(l); s.width = Math.max(s.width, l.width); }
    else slots.push({ pos: l.pos, width: l.width, parts: [l], kind: 'other', tram: false, types: '', invert: false, speedKmh: 0 });
  }
  for (const s of slots) {
    const vt = s.parts.map(p => p.vehicleType).join(',');
    const lt = s.parts.map(p => p.laneType).join(',');
    s.kind = /Parking/.test(lt) ? 'parking'
      : /Bicycle/.test(vt) && !/Car/.test(vt) ? 'bike'
      : /TransportVehicle/.test(lt) && !/(^|,)\s*Vehicle/.test(lt) ? 'bus'
      : /Trolleybus/.test(vt) ? 'trolleybus'
      : /Monorail/.test(vt) ? 'monorail'
      : /Car/.test(vt) ? 'car'
      : /Tram/.test(vt) ? 'tram'
      : 'other';
    s.tram = /Tram/.test(vt);
    s.types = s.parts.map(p => `${p.laneType}/${p.vehicleType}`).join(' + ');
    const dirs = s.parts.map(p => p.direction).join(',');
    s.invert = /Backward/.test(dirs) && !/Forward/.test(dirs);
    s.speedKmh = Math.max(...s.parts.map(p => p.speed ?? 0)) * 50;
  }
  if (overrides && overrides.size) applyOverrides(slots, overrides);
  // Decoration lanes (laneType None) with trees, located between drive slots → median trees.
  const decor = lanes.filter(l => l.laneType === 'None');
  const pedestrianLanes = lanes.filter(l => /Pedestrian/.test(l.laneType)).length;
  const highway = info.aiFields?.m_highwayRules === true && pedestrianLanes === 0;
  return { halfWidth, pavement, slots, decor, pedestrianLanes, highway };
}

/** IMT overrides: Map(lanePos → 'bus' | 'median' | 'median-grass'), positions in NetInfo space. */
function applyOverrides(slots: Slot[], overrides: Map<number, LaneOverrideKind>) {
  for (const [pos, kind] of overrides) {
    const s = slots.find(s => Math.abs(s.pos - pos) < 0.25);
    if (!s) continue;
    s.imt = kind;
    if (kind === 'bus') s.kind = s.kind === 'parking' ? 'bus-on-parking' : 'bus';
    else { s.kind = 'median'; s.grass = kind === 'median-grass'; }
  }
  // Red paint over a parking lane *and* the car lane next to it = one bus lane (parking is gone).
  for (let i = slots.length - 1; i >= 0; i--) {
    if (slots[i].kind !== 'bus-on-parking') continue;
    const nb = [slots[i - 1], slots[i + 1]].find(n => n && n.kind === 'bus' && n.imt === 'bus');
    if (nb) { nb.absorbedParking = slots[i].width; slots.splice(i, 1); }
    else slots[i].kind = 'bus';
  }
}

/** Deterministic RB id, identical to the M0 spike: r<sha1 as uuid v5 shape>-cs1conv. */
export function rbConfigId(infoName: string, sig = ''): string {
  const h = sha1Hex('cs1conv:' + infoName + (sig ? '|' + sig : ''));
  return `r${h.slice(0, 8)}-${h.slice(8, 12)}-5${h.slice(13, 16)}-8${h.slice(17, 20)}-${h.slice(20, 32)}-cs1conv`;
}

/** Display title of a NetInfo (workshop prefix and _Data suffix stripped). */
export function netInfoTitle(info: Pick<NetInfo, 'name' | 'title'>): string {
  return info.title && !/^NET_TITLE\[/.test(info.title) ? info.title : info.name.replace(/^\d+\./, '').replace(/_Data$/, '');
}

/** Generate the RB config for a road NetInfo (+ IMT overrides). null when the road has no drive lane. */
export function convert(info: NetInfo, overrides: Map<number, LaneOverrideKind> | null = null): { config: RbConfig; report: RbReport } | null {
  const a = analyse(info, overrides);
  if (!a.slots.length) return null;
  const notes: string[] = [];
  for (const s of a.slots) {
    if (s.imt === 'bus') notes.push(`IMT red fill covers lane @${s.pos} → bus lane${s.absorbedParking ? ` (paint also covers the ${s.absorbedParking}m parking lane next to it → parking dropped)` : ''}`);
    if (s.imt && s.imt.startsWith('median')) notes.push(`IMT ${s.grass ? 'grass' : 'pavement'} island covers lane @${s.pos} → median`);
  }
  const out: { lane: RbLane; width: number }[] = [];
  const ai = info.aiFields ?? {};
  const addons = new Set<string>(a.highway ? ['HasUndergroundElectricityCable', 'RequiresUpgradeForElectricity'] : ['HasUndergroundWaterPipes', 'HasUndergroundElectricityCable']);
  if (ai.m_trafficLights) addons.add('GeneratesTrafficLights');
  if (!a.highway && ai.m_enableZoning !== false) addons.add('GeneratesZoningBlocks');

  const slots = [...a.slots];
  const takeEdge = (fromLeft: boolean) => {
    const extra = { parking: 0, bike: 0 };
    while (slots.length) {
      const s = fromLeft ? slots[0] : slots[slots.length - 1];
      if (s.kind !== 'parking' && s.kind !== 'bike') break;
      extra[s.kind] += s.width;
      if (fromLeft) slots.shift(); else slots.pop();
    }
    return extra;
  };
  const leftExtra = takeEdge(true), rightExtra = takeEdge(false);

  const sidewalk = (extra: { parking: number; bike: number }, invert: boolean, side: 'left' | 'right') => {
    const kind = extra.parking ? 'P' : extra.bike ? 'B' : '';
    const want = a.pavement + extra.parking + extra.bike;
    const w = nearest(want, SIDEWALK_WIDTHS[kind]);
    if (Math.abs(w - want) > 0.01) notes.push(`${side} sidewalk: CS1 ${want}m (pavement ${a.pavement}m + parking ${extra.parking}m + bike ${extra.bike}m) → RB ${w}m`);
    if (extra.parking && extra.bike) notes.push(`${side}: CS1 has both parking and bike lane; RB sidewalk supports only one → kept parking, dropped bike lane`);
    const sideTrees = a.decor.some(d => d.trees.length && Math.abs(d.pos) >= a.halfWidth - a.pavement - 0.01 && Math.sign(d.pos) === (side === 'left' ? -1 : 1));
    if (sideTrees) {
      if ((w >= 5 && kind !== 'B') || (kind === 'B' && w >= 4.5)) addons.add(side === 'left' ? 'TreesLeft' : 'TreesRight');
      else notes.push(`${side} sidewalk has trees in CS1 but RB ${kind || 'plain'} sidewalk ${w}m cannot carry trees (needs ≥5m)`);
    }
    return { lane: { Version: 5 as const, SectionPrefabName: null, GroupPrefabName: RB + 'SidewalkGroupPrefab', GroupOptions: { Parking: kind, Width: fmtM(w), 'Parking Angle': '' }, Invert: invert }, width: w };
  };
  const shoulder = (extra: { parking: number; bike: number }, invert: boolean, side: 'left' | 'right') => {
    const want = a.pavement;
    const w = nearest(want, SHOULDER_WIDTHS);
    if (Math.abs(w - want) > 0.01) notes.push(`${side} shoulder: CS1 ${want}m → RB ${w}m`);
    if (extra.parking || extra.bike) notes.push(`${side}: highway edge parking/bike lane dropped`);
    return { lane: { Version: 5 as const, SectionPrefabName: null, GroupPrefabName: RB + 'ShoulderGroupPrefab', GroupOptions: { 'Ground Type': 'Asphalt', 'Lane Width': fmtM(w) }, Invert: invert }, width: w };
  };
  const edge = a.highway ? shoulder : sidewalk;

  out.push(edge(leftExtra, true, 'left'));

  // Outermost car lane per direction gets "Transport" (bus/taxi stops attach there), as in the user's own RB roads.
  const carIdx = slots.map((s, i) => (s.kind === 'car' ? i : -1)).filter(i => i >= 0);
  const outer = new Set<number>();
  for (const inv of [true, false]) {
    const same = carIdx.filter(i => slots[i].invert === inv);
    const hasBus = slots.some(s => s.kind === 'bus' && s.invert === inv);
    if (same.length && !hasBus) outer.add(inv ? same[0] : same[same.length - 1]);
  }

  for (let i = 0; i < slots.length; i++) {
    const s = slots[i];
    if (i > 0) {
      const p = slots[i - 1];
      const gap = (s.pos - s.width / 2) - (p.pos + p.width / 2);
      if (gap > 0.6) {
        const trees = a.decor.some(d => d.trees.length && d.pos > p.pos && d.pos < s.pos);
        const grassHint = /grass/i.test(info.name) || /median/i.test(info.name);
        const mw = trees ? 5 : nearest(gap, MEDIAN_WIDTHS);
        if (trees) { addons.add('TreesCenter'); addons.add('GrassCenter'); }
        else if (mw === 5 && grassHint) addons.add('GrassCenter');
        if (Math.abs(mw - gap) > 0.01) notes.push(`median: CS1 gap ${gap.toFixed(2)}m → RB ${mw}m${trees ? ' (5m forced: only 5m medians carry trees)' : ''}`);
        out.push({ lane: { Version: 5, SectionPrefabName: null, GroupPrefabName: RB + 'MedianGroupPrefab', GroupOptions: { 'Median Width': fmtM(mw) }, Invert: false }, width: mw });
      } else if (gap > 0.25) {
        const ew = Math.round(gap * 2) / 2;
        out.push({ lane: { Version: 5, SectionPrefabName: null, GroupPrefabName: RB + 'EmptyGroupPrefab', GroupOptions: { 'Ground Type': 'Asphalt', 'Lane Width': fmtM(ew) }, Invert: false }, width: ew });
        notes.push(`gap ${gap.toFixed(2)}m between lanes → RB Empty ${ew}m`);
      }
    }
    const lw = s.width >= 3.5 ? 4 : 3;
    if (s.kind !== 'median' && Math.abs(lw - s.width) > 0.01) notes.push(`${s.kind} lane @${s.pos}: CS1 ${s.width}m → RB ${lw}m`);
    let lane: Pick<RbLane, 'GroupPrefabName' | 'GroupOptions'>;
    switch (s.kind) {
      case 'car':
        lane = { GroupPrefabName: RB + 'CarGroupPrefab', GroupOptions: { 'Lane Width': fmtM(lw), 'Transport Option': s.tram ? 'Tram' : outer.has(i) ? 'Transport' : '', Markings: '' } };
        break;
      case 'bus':
        lane = { GroupPrefabName: RB + 'BusGroupPrefab', GroupOptions: { 'Lane Width': fmtM(lw), 'Transport Option': s.tram ? 'Tram' : '' } };
        break;
      case 'tram':
        lane = { GroupPrefabName: RB + 'TramGroupPrefab', GroupOptions: {} };
        if (lw !== 3) notes.push('tram lane forced to 3m (only width RB offers)');
        break;
      case 'median': {
        const mw = s.grass ? 5 : nearest(s.width, MEDIAN_WIDTHS);
        if (s.grass) addons.add('GrassCenter');
        if (Math.abs(mw - s.width) > 0.01) notes.push(`IMT median @${s.pos}: CS1 lane ${s.width}m → RB median ${mw}m${s.grass ? ' (5m: only 5m medians carry grass)' : ''}`);
        out.push({ lane: { Version: 5, SectionPrefabName: null, GroupPrefabName: RB + 'MedianGroupPrefab', GroupOptions: { 'Median Width': fmtM(mw) }, Invert: false }, width: mw });
        continue;
      }
      case 'parking':
        lane = { GroupPrefabName: RB + 'ParkingGroupPrefab', GroupOptions: { 'Parking Angle': 'Parallel', Markings: '' } };
        notes.push(`interior parking lane @${s.pos} → RB Parking 2m`);
        break;
      default:
        notes.push(`UNSUPPORTED ${s.kind} lane @${s.pos} (${s.types}) → mapped to car lane`);
        lane = { GroupPrefabName: RB + 'CarGroupPrefab', GroupOptions: { 'Lane Width': fmtM(lw), 'Transport Option': 'Transport', Markings: '' } };
    }
    out.push({ lane: { Version: 5, SectionPrefabName: null, ...lane, Invert: s.invert }, width: s.kind === 'parking' ? 2 : s.kind === 'tram' ? 3 : lw });
  }

  out.push(edge(rightExtra, false, 'right'));

  const speed = Math.max(0, ...a.slots.filter(s => s.kind === 'car' || s.kind === 'bus').map(s => s.speedKmh));
  const rbWidth = out.reduce((t, l) => t + l.width, 0);
  const cs1Width = a.halfWidth * 2;
  const sig = overrideSignature(overrides);
  const id = rbConfigId(info.name, sig);
  // Elevated-only roads: the ground NetInfo may be missing (M0 dump), so the variant stands in.
  const rawTitle = netInfoTitle(info);
  const title = rawTitle.replace(/ (Elevated|Bridge|Slope|Tunnel)\d*$/, '');
  if (title !== rawTitle) notes.push(`only the ${rawTitle.slice(title.length + 1)} variant is in the export; its sidewalks may be narrower than the ground road's`);
  const category = a.highway ? 'Highway' : 'Road';

  const config: RbConfig = {
    Type: 'RoadConfig', Version: 5, OriginalID: id, ID: id,
    Name: `[CS1] ${title}${sig ? ' (IMT ' + [...new Set([...overrides!.values()].map(k => k.replace('median-grass', 'median')))].join('+') + ')' : ''}`.slice(0, 60),
    PillarPrefabName: 'Pillar Placeholder',
    SpeedLimit: Math.round(speed / 5) * 5 * 2 || 100,
    MaxSlopeSteepness: 0.2,
    Category: category,
    Addons: [...addons].join(', '),
    Lanes: out.map(o => o.lane),
    ToolbarState: 'Inherit',
  };
  return {
    config,
    report: {
      cs1: info.name, imtOverrides: sig || undefined, title, id, category, cs1Width, rbWidth, diff: +(rbWidth - cs1Width).toFixed(2), speedKmh: speed, notes,
      cs1Slots: a.slots.map(s => `${s.kind}${s.invert ? '←' : '→'} ${s.width}m @${s.pos}`),
      rbLanes: out.map(o => `${o.lane.GroupPrefabName.replace(RB, '').replace('GroupPrefab', '')}${o.lane.Invert ? '←' : '→'} ${o.width}m ${JSON.stringify(o.lane.GroupOptions)}`),
    },
  };
}
