#!/usr/bin/env node
// Map preparation in a running CS2 city, through the bridge (terrain/cs2-apply MapPrep ops). Each command prints one
// JSON object and can also be called as a module: `await mapprep(['clear'])`.
//
//   node terrain/mapprep.mjs survey [--out file.json]                 what the loaded base map ships with
//   node terrain/mapprep.mjs survey-maps [--maps "A,B"] [--out file]   game.new on every playable map + survey (slow)
//   node terrain/mapprep.mjs clear [--keep-utilities] [--keep-buildings]  delete every top-level net and building of the base map (and with
//                                                                      the road/rail end nodes their outside connections)
//                                                                      before terrain/apply.mjs; --keep-utilities keeps
//                                                                      powerlines, seaways and pipes
//   node terrain/mapprep.mjs connections --cs1x f --plan f [--all-roads] [--dry-run] [--no-utilities]
//                                                                      after the road import: CS1 highways/tracks cut
//                                                                      at the crop edge → CS2 outside connections, then
//                                                                      the electricity/water ones (utilities, below)
//   node terrain/mapprep.mjs utilities [--dry-run] [--max 8]           electricity + water-pipe outside connections on
//                                                                      imported road nodes at the edge of the network
//                                                                      (docs/DECISIONS.md #136; MapPrep oc.utilities)
//   node terrain/mapprep.mjs oc-prefabs | oc-list                      marker prefabs / connections in the city
//   node terrain/mapprep.mjs water --cs1x f --plan f [--terrain dir] [--no-regions]
//                                                                      CS1 water.json → sea level + water sources
//                                                                      (+ inflow/lake sources from the depth grid)
//   node terrain/mapprep.mjs water-reset [--empty]                     reset the water simulation to the sea level
//   node terrain/mapprep.mjs water-check --cs1x f --plan f             CS2 water depth vs CS1's depth grid (wet/dry)
//   node terrain/mapprep.mjs counts                                    vehicles and outside-connection lanes
//
// Build the exec library first: dotnet build -c Release terrain/cs2-apply
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { connect } from '../tools/gamectl/lib/index.js';
import { DLL } from './apply.mjs';
import { deriveConnections } from './lib/connections.mjs';
import { readZip } from './lib/zip.mjs';
import { convertWater, depthCheckpoints, regionSources } from './lib/water.mjs';
import { loadCs1xTerrain, convert, CS2 } from './lib/heightmap.mjs';
import { adjustWaterSources } from './lib/conform.mjs';
import { loadCs1xWater } from './convert.mjs';

const TYPE = 'Cs1ConvTerrain.MapPrep';

function opts(argv) {
  const a = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const k = argv[i];
    if (!k.startsWith('--')) { a._.push(k); continue; }
    const name = k.slice(2).replace(/-([a-z])/g, (_, c) => c.toUpperCase());
    if (i + 1 < argv.length && !argv[i + 1].startsWith('--')) a[name] = argv[++i]; else a[name] = true;
  }
  return a;
}

async function op(bridge, args, { phase, pump, timeout = 300 } = {}) {
  return bridge.exec(DLL, { type: TYPE, args, phase, pump, timeout });
}

export async function mapprep(argv) {
  const a = opts(argv);
  const cmd = a._[0];
  if (!existsSync(DLL)) throw new Error('exec library not built: dotnet build -c Release terrain/cs2-apply');
  const bridge = await connect('cs2');
  let out;
  switch (cmd) {
    case 'survey': out = await op(bridge, { op: 'survey' }); break;
    case 'survey-maps': out = await surveyMaps(bridge, a); break;
    case 'clear': {
      // structural changes must run in ToolUpdate (phase + pump "tool"): from the main-loop pump CS2 crashes natively
      out = await op(bridge, { op: 'clear', frames: 10, all: !a.keepUtilities, keepBuildings: !!a.keepBuildings }, { phase: 'tool', pump: 'tool', timeout: 600 });
      await bridge.wait(60);
      out.check = await op(bridge, { op: 'survey', grid: 5 });
      break;
    }
    case 'oc-prefabs': out = await op(bridge, { op: 'oc.prefabs' }); break;
    case 'oc-list': out = { connections: await op(bridge, { op: 'oc.list' }) }; break;
    case 'connections': {
      if (!a.cs1x || !a.plan) throw new Error('connections needs --cs1x and --plan');
      const plan = JSON.parse(readFileSync(a.plan, 'utf8'));
      const d = deriveConnections(readFileSync(a.cs1x), plan, { allRoads: !!a.allRoads });
      out = { derived: d.counts, skipped: d.skipped };
      if (a.dryRun) { out.connections = d.connections; break; }
      const before = await op(bridge, { op: 'oc.list' });
      const created = await op(bridge, { op: 'oc.create', connections: d.connections.map(c => ({ x: c.x, z: c.z, type: c.type })) }, { phase: 'tool' });
      await bridge.wait(30);
      const after = await op(bridge, { op: 'oc.list' });
      out.before = before.length;
      out.after = after.length;
      out.created = created.results.filter(r => !r.error).length;
      out.errors = created.results.filter(r => r.error);
      out.withLanes = after.filter(c => (c.subLanes ?? 0) > 0).length;
      out.connections = after;
      if (!a.noUtilities) out.utilities = await utilities(bridge, a);
      break;
    }
    case 'utilities': out = await utilities(bridge, a); break;
    case 'water': {
      if (!a.cs1x || !a.plan) throw new Error('water needs --cs1x and --plan');
      const zip = readZip(readFileSync(a.cs1x));
      const we = zip.get('water.json');
      if (!we) throw new Error('.cs1x has no water.json yet');
      const plan = JSON.parse(readFileSync(a.plan, 'utf8'));
      const tf = a.terrain ? ['terrain-cs2.json', 'terrain.json'].map(f => resolve(a.terrain, f)).find(f => existsSync(f)) : null;
      const terrainMeta = tf ? JSON.parse(readFileSync(tf, 'utf8')) : null;
      const wj = JSON.parse(we.read().toString('utf8'));
      const w = convertWater(wj, plan, terrainMeta);
      out = { converted: w.stats };
      if (!a.noRegions && zip.get(wj.depth?.file ?? 'water.r16')) {
        // CS1 rivers/lakes above the sea that no converted source would feed (inflow from outside, unsourced lakes)
        const rs = regionSources(wj.depth, zip.get(wj.depth.file).read(), loadCs1xTerrain(readFileSync(a.cs1x)), plan, wj.seaLevel, w.sources);
        w.sources.push(...rs.sources);
        out.regions = { added: rs.sources.length, byKind: rs.regions.reduce((t, r) => (t[r.kind] = (t[r.kind] || 0) + 1, t), {}), sources: rs.sources };
      }
      // road-conformed / walls-burned terrain (schema §2): sources whose pixel a burn changed keep their absolute level
      if ((plan.terrain?.roadConformed || plan.terrain?.wallsBurned) && !a.noConform) {
        const cs1 = loadCs1xTerrain(readFileSync(a.cs1x));
        const c = convert(cs1.meta, cs1.heights, plan.crop, { transform: plan.transform, plan, water: loadCs1xWater(readFileSync(a.cs1x)) });
        if (c.natural) out.conformAdjusted = adjustWaterSources(w.sources, c.natural, c.burned, CS2.resolution, CS2.size);
      }
      out.set = await op(bridge, { op: 'water.set', seaLevel: w.seaLevel, clear: true, sources: w.sources }, { phase: 'tool' });
      out.reset = await op(bridge, { op: 'water.reset', mode: 'sea' });
      break;
    }
    case 'water-reset': out = await op(bridge, { op: 'water.reset', mode: a.empty ? 'empty' : 'sea' }); break;
    case 'counts': out = await op(bridge, { op: 'counts' }); break;
    case 'water-check': {
      if (!a.cs1x || !a.plan) throw new Error('water-check needs --cs1x and --plan');
      const zip = readZip(readFileSync(a.cs1x));
      const wj = JSON.parse(zip.get('water.json').read().toString('utf8'));
      const plan = JSON.parse(readFileSync(a.plan, 'utf8'));
      const cp = depthCheckpoints(wj.depth, zip.get(wj.depth.file).read(), plan, Number(a.points ?? 150));
      const pts = [...cp.wet, ...cp.dry];
      const r = await op(bridge, { op: 'water.sample', points: pts.map(p => [p.x, p.z]) });
      const rows = pts.map((p, i) => ({ ...p, cs2Depth: Math.round(r.points[i][2] * 100) / 100 }));
      const wet = rows.filter(x => x.cs1Depth > 0.5), dry = rows.filter(x => x.cs1Depth === 0);
      out = {
        seaLevel: r.seaLevel, cs1SeaLevel: wj.seaLevel, totals: cp.totals,
        wetAlsoWetInCs2: wet.filter(x => x.cs2Depth > 0.1).length + '/' + wet.length,
        dryAlsoDryInCs2: dry.filter(x => x.cs2Depth <= 0.1).length + '/' + dry.length,
        wetDepthError: (() => { const e = wet.map(x => Math.abs(x.cs2Depth - x.cs1Depth)).sort((p, q) => p - q); return e.length ? { median: e[e.length >> 1], p90: e[Math.floor(e.length * 0.9)] } : null; })(),
        rows,
      };
      break;
    }
    default: throw new Error('unknown command ' + cmd + ' (survey, survey-maps, clear, connections, utilities, oc-prefabs, oc-list, water, water-reset)');
  }
  if (a.out) writeFileSync(resolve(a.out), JSON.stringify(out, null, 2) + '\n');
  return out;
}

/** Electricity + water-pipe outside connections on the imported roads (#136): MapPrep oc.utilities, then oc.list. */
export async function utilities(bridge, a = {}) {
  const r = await op(bridge, { op: 'oc.utilities', dryRun: !!a.dryRun, max: Number(a.max ?? 8) }, { phase: 'tool' });
  if (a.dryRun) return r;
  await bridge.wait(30);
  const after = await op(bridge, { op: 'oc.list' });
  r.electricityConnections = after.filter(c => c.electricity).length;
  r.waterPipeConnections = after.filter(c => c.waterPipe).length;
  return r;
}

async function surveyMaps(bridge, a) {
  const all = (await bridge.call('maps.list', { limit: 200 })).maps.filter(m => m.playable !== false);
  const wanted = a.maps ? new Set(String(a.maps).split(',').map(s => s.trim())) : null;
  const maps = wanted ? all.filter(m => wanted.has(m.name) || wanted.has(m.id)) : all;
  const results = [];
  for (const m of maps) {
    const t0 = Date.now();
    try {
      await bridge.call('game.new', { map: m.id, name: 'cs1conv-survey', options: { unlockAll: true, unlimitedMoney: true, unlockMapTiles: true } }, { timeout: 900 });
      await bridge.pause();
      await bridge.wait(60);
      const s = await op(bridge, { op: 'survey' });
      results.push({ map: m.name, id: m.id, theme: m.theme, buildable: m.buildable_land, seconds: Math.round((Date.now() - t0) / 1000), ...s });
    } catch (e) {
      results.push({ map: m.name, id: m.id, error: e.message });
    }
    console.error(`[mapprep] surveyed ${m.name}`);
  }
  results.sort((p, q) => score(p) - score(q));
  return { ranking: results.map(r => ({ map: r.map, score: score(r), edges: r.edges, buildings: r.buildings, waterSources: r.water?.sources, seaLevel: r.water?.seaLevel, terrain: r.terrain })), maps: results };
}

/** Lower is better: every net is cleanup, every water source and the sea are water that sits where CS1 has none. */
export function score(r) {
  if (r.error) return Infinity;
  const seaAbove = r.water && r.terrain ? Math.max(0, r.water.seaLevel - r.terrain.min) : 0;
  return (r.edges ?? 0) + 5 * (r.buildings ?? 0) + 50 * (r.water?.sources ?? 0) + (seaAbove > 0 ? 200 : 0);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  mapprep(process.argv.slice(2)).then(o => console.log(JSON.stringify(o, null, 2)), e => { console.error('error: ' + (e.stack || e.message)); process.exitCode = 1; });
}
