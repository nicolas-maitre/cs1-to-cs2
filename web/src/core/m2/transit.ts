// CS1 transport lines → CS2 lines with stops and colours (plan.transit).
//
// CS2 routes a line itself between its stops, so the plan gives the line type, colour and the
// ordered stops. Kerbside stops (bus, tram) are placed on imported roads; station-based lines
// (train, metro, ship, plane) need their station buildings, which come with buildings later, so
// those lines are exported with `requiresStations` for the importer to place once stations exist.

import type { PackageReportItem, PlanTransit, PlanTransitLine } from '../types.ts';
import { plural, r2, tally } from './context.ts';
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
  let outside = 0, kerbside = 0, stations = 0, truncated = 0;
  for (const l of t.lines) {
    const target = transitTarget(l.type);
    if (!target) { tally(noEquivalent, l.type); continue; }
    const stops: PlanTransitLine['stops'] = [];
    let dropped = 0;
    for (const s of l.stops) {
      const p = s.position;
      if (!ctx.inCrop(p[0], p[2])) { dropped++; continue; }
      const onPlanRoad = s.segment != null && ctx.planSegments.has(s.segment);
      if (target.stop && !onPlanRoad) { dropped++; continue; }
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
    if (dropped) items.push({ level: 'warning', kind: 'transit', message: `${ln.name}: ${plural(dropped, 'stop')} outside the crop or off the imported roads were dropped; the line runs between the remaining ${stops.length}`, cs1: { line: l.id } });
    if (target.stop) kerbside++; else stations++;
    lines.push(ln);
  }
  for (const [type, n] of noEquivalent) items.push({ level: 'warning', kind: 'transit', message: `${plural(n, `${type} line`)}: no CS2 equivalent, not converted`, count: n });
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
