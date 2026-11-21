// TM:PE → what CS2 (vanilla) and the Traffic mod can express (plan.traffic), plus the per-segment
// speed limits and bus-only lanes that become Road Builder variants in plan.ts.
//
//   TM:PE                              → plan
//   speed limits (per lane / default)  → RB road variant with that SpeedLimit (one speed per road in CS2)
//   bus-only vehicle restriction       → RB bus lane in that lane (same mechanism as IMT red paint)
//   lane arrows                        → segment-end turn bans (vanilla ForbidLeftTurn/RightTurn/Straight) when
//                                        every inbound lane is customised; always also listed for the Traffic mod
//   lane connections                   → Traffic mod lane connections (lanes by ordinal/offset)
//   priority signs                     → Traffic mod priority signs; all-Stop junctions → vanilla all-way stop
//   traffic lights (node flag)         → per-junction lights on/off (vanilla TrafficLights / RemoveTrafficLights)
//   pedestrian crossing allowed        → vanilla AddCrosswalk / RemoveCrosswalk per segment end
//   u-turns                            → Traffic mod (listed); timed lights, turn on red, blocked-junction,
//                                        lane-changing and parking rules → report only

import type { Cs1City, Cs1Segment, NetInfo, PackageReportItem, PlanTraffic } from '../types.ts';
import { driveLanes, inboundLanes, planLaneRef, plural, tally } from './context.ts';
import type { PlanContext, SectionResult } from './context.ts';

const CAR_TYPES = ['PassengerCar', 'CargoTruck', 'CargoVehicle', 'RoadVehicle'];

/** True when a TM:PE allowed-vehicle set makes a lane a bus lane (buses allowed, private cars and trucks not). */
export function isBusOnly(allowed: string[]): boolean {
  return allowed.includes('Bus') && !allowed.some(a => CAR_TYPES.includes(a));
}

export interface LaneVariants {
  /** segment → (NetInfo lane position → 'bus') from TM:PE bus-only restrictions. */
  bus: Map<number, Map<number, 'bus'>>;
  /** segment → km/h from TM:PE (per-lane custom speed, else the NetInfo's custom default). */
  speed: Map<number, number>;
  items: PackageReportItem[];
}

/** Speed limits and bus lanes for the given segments (before the road plan is built). */
export function trafficLaneVariants(city: Cs1City, infos: Map<string, NetInfo>, segs: Cs1Segment[], enabled = true): LaneVariants {
  const out: LaneVariants = { bus: new Map(), speed: new Map(), items: [] };
  const tm = city.tmpe;
  if (!tm || !enabled) return out;
  const ids = new Set(segs.map(s => s.id));
  const byId = new Map(segs.map(s => [s.id, s]));
  // per-lane speeds → most common value per segment
  const speeds = new Map<number, Map<number, number>>();
  let unlimited = 0;
  for (const s of tm.speedLimits ?? []) {
    if (!ids.has(s.segment)) continue;
    if (s.unlimited || s.kmh >= 1000) { unlimited++; continue; }
    if (!speeds.has(s.segment)) speeds.set(s.segment, new Map());
    tally(speeds.get(s.segment)!, s.kmh);
  }
  let mixed = 0;
  for (const [seg, m] of speeds) {
    const sorted = [...m].sort((a, b) => b[1] - a[1]);
    out.speed.set(seg, sorted[0][0]);
    if (sorted.length > 1) mixed++;
  }
  const defaults = tm.defaultSpeedLimits ?? {};
  for (const s of segs) {
    if (out.speed.has(s.id)) continue;
    const d = defaults[s.info] ?? defaults[infos.get(s.info)?.parent ?? ''];
    if (d != null && d > 0 && d < 20) out.speed.set(s.id, Math.round(d * 50));
  }
  if (mixed) out.items.push({ level: 'info', kind: 'traffic', message: `${plural(mixed, 'segment')} have different TM:PE speed limits per lane; CS2 roads have one speed, the most common one is used`, count: mixed });
  if (unlimited) out.items.push({ level: 'info', kind: 'traffic', message: `${plural(unlimited, 'lane')} with TM:PE "no limit" keep the road's normal speed`, count: unlimited });
  // vehicle restrictions
  const other = new Map<string, number>();
  for (const v of tm.vehicleRestrictions ?? []) {
    if (!ids.has(v.segment)) continue;
    const seg = byId.get(v.segment)!;
    const lane = infos.get(seg.info)?.lanes[v.laneIndex];
    if (lane && isBusOnly(v.allowed)) {
      if (!out.bus.has(v.segment)) out.bus.set(v.segment, new Map());
      out.bus.get(v.segment)!.set(Number(lane.fields.m_position) || 0, 'bus');
    } else tally(other, v.allowed.join('+') || 'nothing');
  }
  for (const [set, n] of other) out.items.push({ level: 'warning', kind: 'traffic', message: `${plural(n, 'lane')} restricted to ${set} by TM:PE: CS2 has no per-lane vehicle restrictions, left open`, count: n });
  return out;
}

const ARROW: Record<string, 'left' | 'straight' | 'right'> = { Left: 'left', Forward: 'straight', Right: 'right' };

export function buildTraffic(ctx: PlanContext, enabled = true): SectionResult<PlanTraffic> {
  const tm = ctx.city.tmpe;
  const items: PackageReportItem[] = [];
  const tr = ctx.t, label = tr('cat.traffic');
  const inPlanEnd = (seg: number, node: number) => ctx.planSegments.has(seg) && ctx.planNodes.has(node);

  // Traffic lights come from the network itself (with or without TM:PE).
  const nodes: PlanTraffic['nodes'] = [];
  for (const id of ctx.planNodes) {
    const n = ctx.nodes.get(id);
    if (!n || !/(^|,\s*)Junction(,|$)/.test(n.flags ?? '')) continue;
    nodes.push({ id, trafficLights: /(^|,\s*)TrafficLights(,|$)/.test(n.flags ?? '') });
  }
  nodes.sort((a, b) => a.id - b.id);
  if (!enabled) return { items, summary: { id: 'traffic', label, status: 'not-converted', summary: tr('sum.off'), details: [] } };
  if (!tm) {
    return {
      section: { requires: [], nodes, segmentEnds: [], laneConnections: [], laneArrows: [] }, items,
      summary: { id: 'traffic', label, status: 'absent', summary: tr('sum.traffic.absent', { n: nodes.filter(n => n.trafficLights).length }), details: [] },
    };
  }

  const ends = new Map<string, PlanTraffic['segmentEnds'][number]>();
  const end = (segment: number, node: number) => {
    const k = `${segment}:${node}`;
    let e = ends.get(k);
    if (!e) { e = { segment, node }; ends.set(k, e); }
    return e;
  };

  // priority signs
  const prio = new Map<number, string[]>();
  let prioCount = 0;
  for (const p of tm.prioritySigns ?? []) {
    if (!inPlanEnd(p.segment, p.node)) continue;
    const t = p.type.toLowerCase();
    if (t !== 'main' && t !== 'stop' && t !== 'yield') continue;
    end(p.segment, p.node).priority = t as 'main' | 'stop' | 'yield';
    if (!prio.has(p.node)) prio.set(p.node, []);
    prio.get(p.node)!.push(t);
    prioCount++;
  }
  let allWay = 0;
  for (const n of nodes) {
    const signs = prio.get(n.id);
    const degree = (ctx.nodes.get(n.id)?.segments ?? []).filter(s => ctx.planSegments.has(s)).length;
    if (signs && signs.length >= 3 && signs.length === degree && signs.every(s => s === 'stop')) { n.allWayStop = true; allWay++; }
  }

  // lane arrows → turn bans when every inbound lane of the segment end is customised
  const arrowsByEnd = new Map<string, { segment: number; node: number; lanes: Set<number>; union: Set<string> }>();
  const laneArrows: PlanTraffic['laneArrows'] = [];
  for (const a of tm.laneArrows ?? []) {
    if (a.node == null || !inPlanEnd(a.segment, a.node)) continue;
    const ref = planLaneRef(ctx, a.segment, a.laneIndex);
    if (!ref) continue;
    laneArrows.push({ node: a.node, lane: ref, arrows: a.arrows.map(x => ARROW[x]).filter(Boolean) });
    const k = `${a.segment}:${a.node}`;
    let e = arrowsByEnd.get(k);
    if (!e) { e = { segment: a.segment, node: a.node, lanes: new Set(), union: new Set() }; arrowsByEnd.set(k, e); }
    e.lanes.add(a.laneIndex);
    for (const x of a.arrows) e.union.add(x);
  }
  let bans = 0, partialEnds = 0;
  for (const e of arrowsByEnd.values()) {
    const inbound = inboundLanes(ctx, e.segment, e.node).filter(l => /Car/.test(l.vehicleType));
    if (!inbound.length || !inbound.every(l => e.lanes.has(l.index))) { partialEnds++; continue; }
    const forbid = (['Left', 'Forward', 'Right'] as const).filter(x => !e.union.has(x)).map(x => ARROW[x]);
    if (forbid.length) { end(e.segment, e.node).forbid = forbid; bans++; }
  }

  // junction restrictions
  const jr = new Map<string, number>();
  let crosswalks = 0, uturns = 0;
  for (const j of tm.junctionRestrictions ?? []) {
    if (!inPlanEnd(j.segment, j.node)) continue;
    if (typeof j.pedestrianCrossingAllowed === 'boolean') { end(j.segment, j.node).crosswalk = j.pedestrianCrossingAllowed; crosswalks++; }
    if (typeof j.uturnAllowed === 'boolean') { end(j.segment, j.node).uturn = j.uturnAllowed; uturns++; }
    for (const k of ['turnOnRedAllowed', 'farTurnOnRedAllowed', 'straightLaneChangingAllowed', 'enterWhenBlockedAllowed']) if (typeof j[k] === 'boolean') tally(jr, k);
  }

  // lane connections
  const laneConnections: PlanTraffic['laneConnections'] = [];
  let trackConn = 0, unresolved = 0;
  for (const c of tm.laneConnections ?? []) {
    if (!ctx.planNodes.has(c.node) || !ctx.planSegments.has(c.sourceSegment) || !ctx.planSegments.has(c.targetSegment)) continue;
    if (c.group && c.group !== 'Road') { trackConn++; continue; }
    const from = planLaneRef(ctx, c.sourceSegment, c.sourceLaneIndex), to = planLaneRef(ctx, c.targetSegment, c.targetLaneIndex);
    if (!from || !to) { unresolved++; continue; }
    laneConnections.push({ node: c.node, from, to });
  }

  // report-only parts
  const timed = (tm.timedTrafficLights ?? []).filter(t => ctx.planNodes.has(t.nodeId)).length;
  const parking = (tm.parkingRestrictions ?? []).filter(p => ctx.planSegments.has(p.segment)).length;
  const added = (tm.trafficLights ?? []).filter(t => ctx.planNodes.has(t.node)).length;
  const segmentEnds = [...ends.values()].sort((a, b) => a.node - b.node || a.segment - b.segment);
  const requires = laneConnections.length || prioCount || laneArrows.length || uturns ? ['Traffic'] : [];

  items.push({ level: 'info', kind: 'traffic', message: `${plural(nodes.length, 'junction')} (${nodes.filter(n => n.trafficLights).length} with traffic lights${added ? `, ${added} set by hand in TM:PE` : ''})` });
  if (prioCount) items.push({ level: 'info', kind: 'traffic', message: `${plural(prioCount, 'priority sign')} → Traffic mod${allWay ? `; ${plural(allWay, 'all-stop junction')} → CS2 all-way stop` : ''}`, count: prioCount });
  if (laneArrows.length) items.push({ level: 'info', kind: 'traffic', message: `${plural(laneArrows.length, 'custom lane arrow')} → Traffic mod; ${plural(bans, 'segment end')} also get CS2 turn bans${partialEnds ? ` (${partialEnds} ends with only some lanes customised get none)` : ''}`, count: laneArrows.length });
  if (laneConnections.length) items.push({ level: 'info', kind: 'traffic', message: `${plural(laneConnections.length, 'lane connection')} → Traffic mod lane connector`, count: laneConnections.length });
  if (crosswalks) items.push({ level: 'info', kind: 'traffic', message: `${plural(crosswalks, 'crosswalk setting')} → CS2 add/remove crosswalk`, count: crosswalks });
  if (trackConn) items.push({ level: 'warning', kind: 'traffic', message: `${plural(trackConn, 'track lane connection')}: not converted`, count: trackConn });
  if (unresolved) items.push({ level: 'warning', kind: 'traffic', message: `${plural(unresolved, 'lane connection')} reference lanes that are not driving lanes; skipped`, count: unresolved });
  if (timed) items.push({ level: 'warning', kind: 'traffic', message: `${plural(timed, 'timed traffic light')}: CS2 runs its own light phases; the junctions keep plain traffic lights`, count: timed });
  for (const [k, n] of jr) items.push({ level: 'info', kind: 'traffic', message: `${plural(n, 'segment end')} with TM:PE "${k.replace('Allowed', '')}": no CS2 setting, not converted`, count: n });
  if (parking) items.push({ level: 'info', kind: 'traffic', message: `${plural(parking, 'parking restriction')}: not converted (the road keeps its parking)`, count: parking });

  const reportOnly = timed + parking + [...jr.values()].reduce((a, b) => a + b, 0) + trackConn;
  return {
    section: { requires, nodes, segmentEnds, laneConnections, laneArrows },
    items,
    summary: {
      id: 'traffic', label, status: reportOnly ? 'partial' : 'converted',
      summary: [
        prioCount && tr('sum.traffic.prio', { n: prioCount }),
        laneConnections.length && tr('sum.traffic.conn', { n: laneConnections.length }),
        laneArrows.length && tr('sum.traffic.arrows', { n: laneArrows.length }),
        tr('sum.traffic.lights', { n: nodes.filter(n => n.trafficLights).length }),
      ].filter(Boolean).join(', '),
      details: [
        tr('sum.traffic.d1'),
        tr('sum.traffic.d2'),
        tr('sum.traffic.d3'),
        ...(timed ? [tr('sum.traffic.d4', { n: timed })] : []),
        tr('sum.traffic.d5'),
      ],
    },
  };
}

/** Driving lanes of the base NetInfo matching a lane position (for applying TM:PE bus lanes). */
export function hasLaneAt(info: NetInfo, pos: number) { return driveLanes(info).some(l => Math.abs(l.position - pos) < 0.25); }
