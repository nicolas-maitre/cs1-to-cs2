// Utilities check (docs/DECISIONS.md #109): build the exec library, let the city simulate, measure, summarise.
// Used by tools/qa/run.mjs and, as a step, by tools/release/acceptance.mjs. Read-only in the city.
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
export const QA_DIR = path.resolve(HERE, '..');
export const QA_DLL = path.join(QA_DIR, 'bin', 'Release', 'net48', 'Cs1ConvQa.dll');

export function buildQa() {
  execFileSync('dotnet', ['build', path.join(QA_DIR, 'Cs1ConvQa.csproj'), '-c', 'Release', '-nologo', '-v', 'q'], { stdio: ['ignore', 'ignore', 'inherit'] });
}

/**
 * Lets the loaded city run `minutes` of real time at top speed (0 = measure as it is), pauses, and returns the op's
 * result. The bridge must have a city loaded.
 */
export async function measureUtilities(bridge, { minutes = 3, listLimit = 60, log = () => {} } = {}) {
  if (minutes > 0) {
    log(`simulating ${minutes} min at speed 3`);
    await bridge.call('sim.set', { speed: 3, paused: false });
    await bridge.wait(undefined, minutes * 60);
    await bridge.call('sim.set', { paused: true });
  }
  return bridge.exec(QA_DLL, { type: 'Cs1ConvQa.Utilities', args: { listLimit }, timeout: 120 });
}

const pct = (a, b) => (b ? `${((100 * a) / b).toFixed(1)} %` : '–');

/** One line per finding, and ok=false when something needs the player's or a worker's attention. */
export function summariseUtilities(r) {
  const lines = [];
  const cov = r.coverage ?? {};
  for (const [cat, c] of Object.entries(cov)) {
    if (!c.buildings) continue;
    lines.push(`${cat}: ${c.buildings} buildings; electricity ${pct(c.electricitySupplied, c.electricityConsumers)} supplied (${pct(c.electricityConnected, c.electricityConsumers)} connected), water ${pct(c.waterSupplied, c.waterConsumers)} (${pct(c.waterConnected, c.waterConsumers)} connected), sewage ${pct(c.sewageServed, c.waterConsumers)} (${pct(c.sewageConnected, c.waterConsumers)} connected)`);
  }
  // Households next to the residential building count (QA §12): CS2 sometimes stalls residential spawning in a fresh city
  // (Skibidi: 7 homes in one run, 667 in the next, same build). Informational; it never fails the check.
  if (r.households != null) {
    const homes = cov.residential?.buildings ?? 0;
    lines.push(`households: ${r.households} (${r.homelessHouseholds ?? 0} homeless) in ${homes} residential buildings${homes < 50 && r.households > 4 * homes ? ' – few homes for the households moving in: CS2 spawn variance, not a connection problem (see the supply above)' : ''}`);
  }
  const e = r.totals?.electricity, w = r.totals?.water, t = r.totals?.electricityTrade;
  if (e) lines.push(`electricity: production ${e.production}, consumption ${e.consumption} (fulfilled ${e.fulfilledConsumption})${t ? `, import ${t.import}, export ${t.export}` : ''}`);
  if (w) lines.push(`water: capacity ${w.freshCapacity}, consumption ${w.freshConsumption} (fulfilled ${w.fulfilledFreshConsumption}); sewage: capacity ${w.sewageCapacity}, consumption ${w.sewageConsumption} (fulfilled ${w.fulfilledSewageConsumption})`);
  const f = r.facilities ?? {};
  lines.push(`facilities: ${f.power ?? 0} power, ${f.pumps ?? 0} pumps, ${f.outlets ?? 0} outlets, ${f.towers ?? 0} towers`);
  for (const [k, v] of Object.entries(r.problems ?? {})) {
    if (!v.count) continue;
    lines.push(`${k}: ${v.count} (${v.items.slice(0, 6).map((i) => `${i.prefab}${i.position ? ` @ ${Math.round(i.position[0])},${Math.round(i.position[2])}` : ''}${i.notifications?.length ? ` [${i.notifications.join(', ')}]` : ''}`).join('; ')}${v.count > 6 ? '; …' : ''})`);
  }
  const o = r.outsideConnections ?? {};
  lines.push(`outside connections: electricity ${o.electricity ?? 0}, water ${o.water ?? 0} (all kinds ${o.all ?? 0})`);
  const consumers = Object.values(cov).reduce((s, c) => s + (c.electricityConsumers ?? 0), 0);
  const supplied = Object.values(cov).reduce((s, c) => s + (c.electricitySupplied ?? 0), 0);
  const water = Object.values(cov).reduce((s, c) => s + (c.waterSupplied ?? 0), 0), wc = Object.values(cov).reduce((s, c) => s + (c.waterConsumers ?? 0), 0);
  const sewage = Object.values(cov).reduce((s, c) => s + (c.sewageServed ?? 0), 0);
  const share = { electricity: consumers ? supplied / consumers : null, water: wc ? water / wc : null, sewage: wc ? sewage / wc : null };
  const problems = Object.values(r.problems ?? {}).reduce((s, v) => s + (v.count ?? 0), 0);
  // a threshold, not a pass/fail on CS2's economy: below 90 % something structural is missing
  const ok = problems === 0 && Object.values(share).every((s) => s == null || s >= 0.9);
  return { ok, share, problems, lines };
}
