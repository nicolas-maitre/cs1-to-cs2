#!/usr/bin/env node
// Rail round (2026-11-22): on a loaded save (never saved), for every train and metro line: edge reach between stations
// (lines.reach), the directed lane graph from each yard (lanes.reach), the yards, and vehicles per line.
//   node transit/tools/rail-reach.mjs <save> [out] ["x,z;x,z" edges.at points]
import { writeFileSync } from 'node:fs';
import { start, stop } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
import { holdCs2, freeCs2 } from '../../terrain/lib/cs2lock.mjs';
const [save, outFile = '.gamectl/tracklink/rail-reach.json', pts = ''] = process.argv.slice(2); // pts: "x,z;x,z" for edges.at too
const SESSION = 'terrain worker (transit/tools/rail-reach.mjs)', OWNER = SESSION;
await holdCs2(OWNER, SESSION, { log: (m) => console.error(m) });
const out = { save };
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 }); await b.pause();
  const x = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600, ...o });
  const lines = await x({ op: 'lines.list' });
  out.lines = lines.filter(l => /Train|Subway/.test(l.prefab)).map(l => ({ name: l.name, prefab: l.prefab, vehicles: l.vehicles, moving: l.movingVehicles }));
  out.edgeReach = await x({ op: 'lines.reach' });
  out.laneReach = {};
  for (const l of out.lines) {
    try { out.laneReach[l.name] = await x({ op: 'lanes.reach', name: l.name, track: /Train/.test(l.prefab) ? 'Train' : 'Subway' }); }
    catch (e) { out.laneReach[l.name] = { error: e.message }; }
  }
  out.depots = await x({ op: 'depots.check' });
  // DRYLINK=1: the mod's tracklink as a dry run on yards only (no change to the city): which bridge candidates and why not
  if (process.env.DRYLINK) out.dryLink = await x({ op: 'tracklink', dryRun: true, stations: false, bridge: false, maxDistance: 2500, tangentLinks: true, yardLinkPrefab: 'yard', avoidRoads: true, guardRoads: true, bridgeYard: true, frontOnly: true }, { phase: 'tool', pump: 'tool' });
  // the yard's joints (its sub-net nodes with a link) and the edges around it (the bridge pieces)
  out.yardJoints = await x({ op: 'lanes.joint' });
  const yard = (out.depots.depots ?? []).find(d => /RailYard|SubwayYard/.test(d.prefab));
  if (pts) out.edgesAt = await x({ op: 'edges.at', points: pts.split(';').map(p => p.split(',').map(Number)), r: 30 });
  if (yard) out.yardEdges = await x({ op: 'edges.at', points: [[yard.pos[0], yard.pos[2]]], r: 250 });
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  await freeCs2(OWNER);
  writeFileSync(outFile, JSON.stringify(out, null, 1));
  console.log(outFile);
}
