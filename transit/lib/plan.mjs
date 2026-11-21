// plan.transit → jobs for the CS2 exec library (transit/cs2-exec): kerbside stops to create, and lines.
// See schema/README.md §2 "transit" and transit/NOTES-cs2-transit.md.

/** Cubic bezier point and tangent at t (arrays of [x,y,z]). */
export function bezierAt(b, t) {
  const u = 1 - t;
  const p = [0, 1, 2].map(i => u * u * u * b[0][i] + 3 * u * u * t * b[1][i] + 3 * u * t * t * b[2][i] + t * t * t * b[3][i]);
  const d = [0, 1, 2].map(i => 3 * u * u * (b[1][i] - b[0][i]) + 6 * u * t * (b[2][i] - b[1][i]) + 3 * t * t * (b[3][i] - b[2][i]));
  return { p, d };
}

const r2 = v => Math.round(v * 100) / 100;

/** The plan names generic stop prefabs; CS2's are per theme (EU_/NA_). */
export const STOP_PREFABS = {
  EU: { 'Bus Stop': 'EU_BusStop01', 'Tram Stop': 'EU_TramStop01' },
  NA: { 'Bus Stop': 'NA_BusStop01', 'Tram Stop': 'NA_TramStop01' },
};
const r4 = v => Math.round(v * 1e4) / 1e4;

/**
 * Returns {stops: [{key, x, z, tx, tz, side, prefab}], lines: [{key, name, number, prefab, color, requiresStations,
 * vehicles, stops: [{x, z, key?}]}], skipped: [...], stats}.
 * Kerbside stops are keyed by (segment, side, t) so lines that share a CS1 stop share one CS2 stop.
 * Their x/z is the plan segment's centre line at t (the edge search point); tx/tz the plan direction there.
 * Station lines keep the CS1 stop position; the exec step looks for a station stop of the line's type nearby.
 */
export function transitJobs(plan, { only, theme = 'EU' } = {}) {
  const stopNames = STOP_PREFABS[theme] || STOP_PREFABS.EU;
  const transit = plan.transit;
  const out = { stops: [], lines: [], skipped: [], stats: { lines: 0, bus: 0, station: 0, kerbStops: 0, sharedStops: 0 } };
  if (!transit?.lines?.length) return out;
  const segs = new Map((plan.segments || []).map(s => [s.id, s]));
  const byKey = new Map();
  for (const l of transit.lines) {
    if (only && !only.includes(l.name) && !only.includes(String(l.cs1Id))) continue;
    out.stats.lines++;
    const line = {
      key: 'cs1-' + l.cs1Id, name: l.name, number: l.number, prefab: l.line, color: l.color,
      requiresStations: !!l.requiresStations, vehicles: l.vehicles ?? null, truncated: !!l.truncated, stops: [],
    };
    if (l.requiresStations) {
      out.stats.station++;
      for (const s of l.stops) line.stops.push({ x: s.x, z: s.z, cs1Building: s.cs1Building ?? 0 });
    } else {
      out.stats.bus++;
      for (const s of l.stops) {
        const seg = segs.get(s.segment);
        if (!seg) { out.skipped.push({ line: l.name, stop: s.cs1Node, reason: 'segment not in plan' }); continue; }
        const key = `${s.segment}:${s.side}:${s.t}`;
        if (!byKey.has(key)) {
          const { p, d } = bezierAt(seg.bezier, s.t);
          const len = Math.hypot(d[0], d[2]) || 1;
          const stop = { key, x: r2(p[0]), z: r2(p[2]), tx: r4(d[0] / len), tz: r4(d[2] / len), side: s.side, prefab: stopNames[l.stopPrefab || 'Bus Stop'] || l.stopPrefab, segment: s.segment, cs1Node: s.cs1Node };
          byKey.set(key, stop);
          out.stops.push(stop);
          out.stats.kerbStops++;
        } else out.stats.sharedStops++;
        const st = byKey.get(key);
        line.stops.push({ x: st.x, z: st.z, key });
      }
    }
    out.lines.push(line);
  }
  return out;
}
