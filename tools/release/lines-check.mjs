// Vehicles on the imported transit lines (docs/DECISIONS.md #164): the acceptance's verdict, game-free so it can be
// tested. A city counted 31–34 buses by prefab while every one of them stood parked in the depot and every line was
// NotEnoughVehicles; only vehicles running on a line's route count.
//
// Input: the transit library's `lines.list` (each line: name, prefab, movingVehicles = lead vehicles whose CurrentRoute
// is the line, not parked, not DummyTraffic) and its `counts` → movingByPrefab (the same filter, by vehicle prefab).

/** Line kind from its prefab ("Bus Line", "Tram Line", "Subway Line", "Train Line", …). */
export function lineKind(prefab) {
  const p = String(prefab ?? '');
  if (/subway|metro/i.test(p)) return 'metro';
  if (/train|rail/i.test(p)) return 'train';
  if (/tram/i.test(p)) return 'tram';
  if (/trolley/i.test(p)) return 'trolleybus';
  if (/bus/i.test(p)) return 'bus';
  return 'other';
}

/** Vehicle kind from its prefab name (Bus01, NA_TrainPassengerEngine01, SubwayCar01, …). */
export function vehicleKind(name) {
  const n = String(name ?? '');
  if (/subway|metro/i.test(n)) return 'metro';
  if (/train|engine|locomotive/i.test(n)) return 'train';
  if (/tram/i.test(n)) return 'tram';
  if (/bus/i.test(n)) return 'bus';
  return 'other';
}

/** Kinds whose lines must run a vehicle: road lines. Rail lines are reported only while rail is opt-in (#164). */
export const MUST_RUN = new Set(['bus', 'tram', 'trolleybus']);

/**
 * @param {{name?: string, prefab?: string, movingVehicles?: number}[]} lines
 * @param {Record<string, number>} [movingByPrefab]
 * @param {{prefab?: string, available?: number, ownedVehicles?: number}[]} [depots] – the transit library's depots.check
 *   depots. When every depot of a line kind has dispatched all its vehicles (available 0), an idle line of that kind is
 *   a capacity limit (CS1 cities often have one depot for many lines), reported but not failed.
 * @returns {{ok: boolean, error?: string, byKind: object, moving: object, idle: string[], capacityLimited: string[], summary: string[]}}
 */
export function evaluateLines(lines, movingByPrefab, depots) {
  const list = Array.isArray(lines) ? lines : [];
  if (list.some((l) => typeof l.movingVehicles !== 'number')) {
    return { ok: false, error: 'the transit library reports no movingVehicles per line (lines.list): rebuild transit/cs2-exec', byKind: {}, moving: {}, idle: [], capacityLimited: [], summary: [] };
  }
  // depot kinds whose depots are all out of vehicles
  const depotKind = (p) => (/^BusDepot/i.test(p) ? 'bus' : /^TramDepot/i.test(p) ? 'tram' : /^Trolley/i.test(p) ? 'trolleybus' : null);
  const fleet = {};
  for (const d of Array.isArray(depots) ? depots : []) {
    const k = depotKind(String(d.prefab ?? ''));
    if (!k) continue;
    const f = (fleet[k] ??= { depots: 0, available: 0, owned: 0 });
    f.depots++; f.available += d.available ?? 0; f.owned += d.ownedVehicles ?? 0;
  }
  const exhausted = (k) => fleet[k] && fleet[k].depots > 0 && fleet[k].available === 0 && fleet[k].owned > 0;
  const capacityLimited = [];
  const byKind = {};
  const idle = [];
  for (const l of list) {
    const k = lineKind(l.prefab);
    const b = (byKind[k] ??= { lines: 0, withVehicles: 0, vehicles: 0 });
    b.lines++;
    b.vehicles += l.movingVehicles;
    if (l.movingVehicles > 0) b.withVehicles++;
    else if (MUST_RUN.has(k) && exhausted(k)) capacityLimited.push(`${l.name ?? '(unnamed)'} (${l.prefab})`);
    else if (MUST_RUN.has(k)) idle.push(`${l.name ?? '(unnamed)'} (${l.prefab})`);
  }
  const moving = {};
  for (const [n, c] of Object.entries(movingByPrefab ?? {})) moving[vehicleKind(n)] = (moving[vehicleKind(n)] ?? 0) + (typeof c === 'number' ? c : (c?.count ?? 0));
  const label = { bus: 'Bus', tram: 'Tram', trolleybus: 'Trolleybus', metro: 'Metro', train: 'Train', other: 'Other' };
  const summary = Object.entries(byKind).map(([k, b]) => `${label[k] ?? k} lines with vehicles: ${b.withVehicles} of ${b.lines} (${b.vehicles} running)${MUST_RUN.has(k) ? '' : ', reported only'}`);
  summary.push(`Moving vehicles: ${Object.entries(moving).map(([k, c]) => `${c} ${k}`).join(', ') || 'none'}`);
  for (const [k, f] of Object.entries(fleet)) if (exhausted(k) && capacityLimited.length) summary.push(`${label[k] ?? k} depots out of vehicles (${f.owned} owned, 0 available in ${f.depots} depot(s)): ${capacityLimited.length} line(s) wait for capacity, not failed`);
  return { ok: idle.length === 0, byKind, moving, idle, capacityLimited, summary };
}
