// CS1 transport lines → CS2 lines with stops and colours (plan.transit).
//
// CS2 routes a line itself between its stops, so the plan gives the line type, colour and the
// ordered stops. Kerbside stops (bus, tram) are placed on imported roads; station-based lines
// (train, metro, ship, plane) need their station buildings, which come with buildings later, so
// those lines are exported with `requiresStations` for the importer to place once stations exist.

import type { PackageReportItem, PlanTransit, PlanTransitLine } from '../types.ts';
import { driveLanes, plural, r2, tally } from './context.ts';
import type { PlanContext, SectionResult } from './context.ts';

export interface TransitTarget {
  /** CS2 transport line prefab. */
  line: string;
  /** CS2 kerbside stop prefab, or null when stops are stations (buildings). */
  stop: string | null;
  /** true when the CS1 type is approximated (e.g. trolleybus → bus). */
  approx?: string;
}

/** CS1 TransportType → CS2 line (names from the CS2 prefab list). null = no CS2 equivalent. */
export function transitTarget(cs1Type: string): TransitTarget | null {
  switch (cs1Type) {
    case 'Bus': return { line: 'Bus Line', stop: 'Bus Stop' };
    case 'TouristBus': return { line: 'Bus Line', stop: 'Bus Stop', approx: 'sightseeing bus → regular bus line' };
    case 'IntercityBus': return { line: 'Bus Line', stop: 'Bus Stop', approx: 'intercity bus → regular bus line' };
    case 'Trolleybus': return { line: 'Bus Line', stop: 'Bus Stop', approx: 'CS2 has no trolleybuses → bus line' };
    case 'Tram': return { line: 'Tram Line', stop: 'Tram Stop' };
    case 'Metro': return { line: 'Subway Line', stop: null };
    case 'Train': return { line: 'Passenger Train Line', stop: null };
    case 'Ship': return { line: 'Passenger Ship Line', stop: null };
    case 'Ferry': return { line: 'Passenger Ship Line', stop: null, approx: 'ferry → passenger ship line' };
    case 'Airplane': return { line: 'Passenger Airplane Line', stop: null };
    default: return null; // Monorail, CableCar, Helicopter, Blimp, Pedestrian (walking tours), Taxi, Post, …
  }
}

/** A dead end this close to the crop edge (m) is a road the crop cut, not a cul-de-sac: nothing turns round there. */
export const EDGE_DEAD_END_M = 50;

/**
 * Kerbside stops a bus or tram can't serve because of the crop (docs/DECISIONS.md #166 follow-up). In CS1 the line
 * left the crop and came back; the roads crossing the edge are dropped, so a stop whose road only leads to the crop
 * edge strands the vehicle (or a stop only reachable from the edge is never reached).
 *
 * The road graph: one state per plan segment and travel direction, where the segment's car lanes allow it (CS1 frame:
 * a Forward lane flows start → end, mirrored on inverted segments, as for lane connections). From a state a vehicle
 * continues on any other car segment at the node it reaches, in a direction the lanes allow; no U-turns at junctions.
 * At a dead end of a two-way road it turns round (CS2 does, even across a median); the end of a one-way road is a
 * real dead end. A stop is dropped when every way on from it ends at a dead end within EDGE_DEAD_END_M of the crop
 * edge, with no loop to circulate on ("exit"), or when it can only be reached from one ("entry"). Dead ends far from
 * the edge (CS1's own) don't drop anything, and lines on an island with its own loops keep their stops. Skibidi 3 km:
 * Bus Line 14 loses the stop on its one-way chain to the edge; Bus Lines 3, 15 and 16, whose stops sit on two-way
 * stubs to the edge, run in game and keep them.
 */
export function cropTrappedStops(ctx: PlanContext): (segment: number, laneIndex: number | null | undefined) => 'exit' | 'entry' | null {
  const carLanes = (segId: number) => {
    const seg = ctx.segments.get(segId);
    return seg ? driveLanes(ctx.infos.get(seg.info)).filter(l => /Car/.test(l.vehicleType)) : [];
  };
  // per car segment: CS1 start/end node and which directions its lanes allow
  const segs = new Map<number, { a: number; b: number; ab: boolean; ba: boolean; inverted: boolean }>();
  const atNode = new Map<number, number[]>();
  for (const id of ctx.planSegments) {
    const seg = ctx.segments.get(id);
    const lanes = carLanes(id);
    if (!seg || !lanes.length) continue;
    let ab = false, ba = false;
    for (const l of lanes) {
      if (/Both/.test(l.direction)) { ab = ba = true; continue; }
      const forward = /Forward/.test(l.direction) !== !!seg.inverted;
      if (forward) ab = true; else ba = true;
    }
    segs.set(id, { a: seg.startNode, b: seg.endNode, ab, ba, inverted: !!seg.inverted });
    for (const n of [seg.startNode, seg.endNode]) { const l = atNode.get(n); if (l) l.push(id); else atNode.set(n, [id]); }
  }
  const h = ctx.crop.size / 2;
  const edgeDeadEnd = (n: number) => {
    if ((atNode.get(n)?.length ?? 0) !== 1) return false;
    const p = ctx.nodes.get(n)?.position;
    if (!p) return false;
    return Math.min(h - Math.abs(p[0] - ctx.crop.centerX), h - Math.abs(p[2] - ctx.crop.centerZ)) <= EDGE_DEAD_END_M;
  };
  // states: key = segment * 2 + (0: a → b, 1: b → a)
  const key = (seg: number, rev: boolean) => seg * 2 + (rev ? 1 : 0);
  const states: number[] = [];
  for (const [id, s] of segs) { if (s.ab) states.push(key(id, false)); if (s.ba) states.push(key(id, true)); }
  const stateSet = new Set(states);
  const headOf = (k: number) => { const s = segs.get(Math.floor(k / 2))!; return k % 2 ? s.a : s.b; };
  const tailOf = (k: number) => { const s = segs.get(Math.floor(k / 2))!; return k % 2 ? s.b : s.a; };
  const next = (k: number): number[] => {
    const seg = Math.floor(k / 2), n = headOf(k), out: number[] = [];
    const around = atNode.get(n) ?? [];
    for (const o of around) {
      if (o === seg) continue;
      const s = segs.get(o)!;
      const k2 = key(o, s.a !== n); // leave n along o
      if (stateSet.has(k2)) out.push(k2);
    }
    // a two-way road's dead end turns vehicles round (CS2 does it even across a median: Skibidi's Bus Lines 3, 15 and
    // 16 run that way in game); the end of a one-way road has no lane back, so it stays a dead end
    if (around.length === 1) { const back = key(seg, k % 2 === 0); if (stateSet.has(back)) out.push(back); }
    return out;
  };
  const prev = new Map<number, number[]>();
  for (const k of states) for (const k2 of next(k)) { const l = prev.get(k2); if (l) l.push(k); else prev.set(k2, [k]); }
  // states on a cycle (Tarjan, iterative): a vehicle there can circulate
  const index = new Map<number, number>(), low = new Map<number, number>(), onStack = new Set<number>(), stack: number[] = [];
  const cyclic = new Set<number>();
  let counter = 0;
  for (const root of states) {
    if (index.has(root)) continue;
    const work: [number, number[], number][] = [[root, next(root), 0]];
    index.set(root, counter); low.set(root, counter); counter++; stack.push(root); onStack.add(root);
    while (work.length) {
      const top = work[work.length - 1];
      const [v, succ] = top;
      if (top[2] < succ.length) {
        const w = succ[top[2]++];
        if (!index.has(w)) { index.set(w, counter); low.set(w, counter); counter++; stack.push(w); onStack.add(w); work.push([w, next(w), 0]); }
        else if (onStack.has(w)) low.set(v, Math.min(low.get(v)!, index.get(w)!));
        continue;
      }
      work.pop();
      if (work.length) { const p = work[work.length - 1][0]; low.set(p, Math.min(low.get(p)!, low.get(v)!)); }
      if (low.get(v) === index.get(v)) {
        const comp: number[] = [];
        let w: number;
        do { w = stack.pop()!; onStack.delete(w); comp.push(w); } while (w !== v);
        if (comp.length > 1 || next(v).includes(v)) for (const c of comp) cyclic.add(c);
      }
    }
  }
  const spread = (seeds: Iterable<number>, step: (k: number) => number[]) => {
    const seen = new Set<number>(seeds), q = [...seen];
    while (q.length) for (const k2 of step(q.pop()!)) if (!seen.has(k2)) { seen.add(k2); q.push(k2); }
    return seen;
  };
  const canCirculate = spread(cyclic, k => prev.get(k) ?? []); // reaches a loop
  const fromLoops = spread(cyclic, next); // reached from a loop
  const touchesEdgeDeadEnd = (from: number, step: (k: number) => number[], end: (k: number) => number) => {
    for (const k of spread([from], step)) if (edgeDeadEnd(end(k))) return true;
    return false;
  };
  return (segment, laneIndex) => {
    const s = segs.get(segment);
    if (!s) return null;
    // the stop's travel direction: the car lane nearest to the stop's own lane (usually the sidewalk beside it)
    const seg = ctx.segments.get(segment)!;
    const stopLane = laneIndex != null ? ctx.infos.get(seg.info)?.lanes[laneIndex] : undefined;
    const lanes = carLanes(segment);
    if (!stopLane || !lanes.length) return null;
    const pos = Number(stopLane.fields.m_position) || 0;
    const near = lanes.reduce((b, l) => (Math.abs(l.position - pos) < Math.abs(b.position - pos) ? l : b));
    if (/Both/.test(near.direction)) return null;
    const k = key(segment, (/Forward/.test(near.direction) !== s.inverted) === false);
    if (!stateSet.has(k)) return null;
    if (!canCirculate.has(k) && touchesEdgeDeadEnd(k, next, headOf)) return 'exit';
    if (!fromLoops.has(k) && touchesEdgeDeadEnd(k, x => prev.get(x) ?? [], tailOf)) return 'entry';
    return null;
  };
}

export const hexColor = (c?: number[]) => (c && c.length >= 3 ? '#' + c.slice(0, 3).map(v => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')).join('') : null);

export function buildTransit(ctx: PlanContext, enabled = true): SectionResult<PlanTransit> {
  const t = ctx.city.transit;
  const items: PackageReportItem[] = [];
  const tr = ctx.t, label = tr('cat.transit');
  if (!t) return { items, summary: { id: 'transit', label, status: 'absent', summary: tr('sum.transit.absent'), details: [] } };
  if (!t.lines.length) return { items, summary: { id: 'transit', label, status: 'none', summary: tr('sum.transit.none'), details: [] } };
  if (!enabled) return { items, summary: { id: 'transit', label, status: 'not-converted', summary: tr('sum.off'), details: [] } };
  const lines: PlanTransitLine[] = [];
  const noEquivalent = new Map<string, number>();
  let outside = 0, kerbside = 0, stations = 0, truncated = 0, edgeStops = 0;
  let trapped: ReturnType<typeof cropTrappedStops> | null = null;
  for (const l of t.lines) {
    const target = transitTarget(l.type);
    if (!target) { tally(noEquivalent, l.type); continue; }
    const stops: PlanTransitLine['stops'] = [];
    let dropped = 0;
    const edgeDropped: string[] = [];
    for (const [i, s] of l.stops.entries()) {
      const p = s.position;
      if (!ctx.inCrop(p[0], p[2])) { dropped++; continue; }
      const onPlanRoad = s.segment != null && ctx.planSegments.has(s.segment);
      if (target.stop && !onPlanRoad) { dropped++; continue; }
      if (target.stop) {
        // a kerbside stop whose road only leads to (or only comes from) the crop edge: the vehicle would be stranded
        trapped ??= cropTrappedStops(ctx);
        const why = trapped(s.segment!, s.laneIndex);
        if (why) {
          dropped++; edgeStops++;
          edgeDropped.push(`stop ${i + 1} (CS1 segment ${s.segment}) dropped: its road only ${why === 'exit' ? 'leads to' : 'comes from'} the crop edge`);
          continue;
        }
      }
      let side: 'left' | 'right' | null = null, tt: number | null = null;
      if (s.segment != null && s.laneIndex != null) {
        const seg = ctx.segments.get(s.segment);
        const lane = seg ? ctx.infos.get(seg.info)?.lanes[s.laneIndex] : undefined;
        if (lane) side = Number(lane.fields.m_position) >= 0 ? 'right' : 'left';
        if (s.laneOffset != null) tt = r2(ctx.reversed.has(s.segment) ? 1 - s.laneOffset : s.laneOffset);
      }
      stops.push({
        cs1Node: s.node, x: r2(p[0] + ctx.dx), z: r2(p[2] + ctx.dz), y: r2(p[1]),
        segment: onPlanRoad ? s.segment! : null, side, t: tt, cs1Building: s.building || 0,
      });
    }
    if (stops.length < 2) { outside++; continue; }
    if (dropped) truncated++;
    const ln: PlanTransitLine = {
      cs1Id: l.id, name: l.name ?? `${l.type} ${l.number ?? l.id}`, number: l.number ?? null, cs1Type: l.type,
      line: target.line, stopPrefab: target.stop, color: hexColor(l.color), vehicles: l.vehicles ?? null,
      requiresStations: !target.stop, truncated: dropped > 0, stops,
    };
    if (target.approx) items.push({ level: 'info', kind: 'transit', message: `${ln.name}: ${target.approx}`, cs1: { line: l.id } });
    if (dropped > edgeDropped.length) items.push({ level: 'warning', kind: 'transit', message: `${ln.name}: ${plural(dropped - edgeDropped.length, 'stop')} outside the crop or off the imported roads were dropped; the line runs between the remaining ${stops.length}`, cs1: { line: l.id } });
    for (const m of edgeDropped) items.push({ level: 'warning', kind: 'transit', message: `${ln.name}: ${m}`, cs1: { line: l.id } });
    if (target.stop) kerbside++; else stations++;
    lines.push(ln);
  }
  for (const [type, n] of noEquivalent) items.push({ level: 'warning', kind: 'transit', message: `${plural(n, `${type} line`)}: no CS2 equivalent, not converted`, count: n });
  if (edgeStops) items.push({ level: 'info', kind: 'transit', message: `${plural(edgeStops, 'kerbside stop')} on one-way roads that only lead to or come from the crop edge ${edgeStops === 1 ? 'was' : 'were'} dropped: in CS1 the line left the crop there and came back, and the roads crossing the edge aren't imported`, count: edgeStops });
  if (outside) items.push({ level: 'info', kind: 'transit', message: `${plural(outside, 'line')} with fewer than two stops in the crop are left out`, count: outside });
  if (stations) items.push({ level: 'info', kind: 'transit', message: `${plural(stations, 'train, metro, ship or plane line')} need their stations; the importer creates them once stations exist (buildings come later)`, count: stations });
  const skipped = [...noEquivalent.values()].reduce((a, b) => a + b, 0);
  return {
    section: { lines },
    items,
    summary: {
      id: 'transit', label,
      status: !lines.length ? 'not-converted' : skipped || truncated || stations ? 'partial' : 'converted',
      summary: lines.length ? tr('sum.transit', { n: kerbside }) + (stations ? `, ${tr('sum.transit.stations', { n: stations })}` : '') : tr('sum.transit.noneInCrop'),
      details: [
        tr('sum.transit.d1'),
        tr('sum.transit.d2'),
        ...(stations ? [tr('sum.transit.d3')] : []),
        ...(skipped ? [tr('sum.transit.d4', { n: skipped })] : []),
        ...(truncated ? [tr('sum.transit.d5', { n: truncated })] : []),
      ],
    },
  };
}
