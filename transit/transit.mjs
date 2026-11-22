#!/usr/bin/env node
// CS1 transit lines (plan.transit) → CS2 stops and lines in the running city, through the bridge. Prints one JSON
// object; also callable as a module: `await transit(['apply', '--plan', p])`.
//
//   node transit/transit.mjs apply --plan <cs1conv-plan.json> [--only "Bus Line 5,Metro Line 3"] [--theme EU|NA]
//        after the road import (and after the buildings, for stations and depots): creates the kerbside stops,
//        waits, then creates the lines (bus lines on those stops; metro/train lines on station stops near the CS1
//        stations, skipped when none are found). Writes nothing else.
//   node transit/transit.mjs depots --plan <cs1conv-plan.json>
//        after apply and the buildings: metro/train yard at the crop edge where that type's track leaves the crop
//        (yards.plan → buildings placer → tracklink), or report entries "place a depot yourself" (DECISIONS.md #83)
//   node transit/transit.mjs plan --plan f          offline: what apply would do (counts, stops, lines)
//   node transit/transit.mjs prefabs | list | stops [--type Bus] | counts
//
// Build the exec library first: dotnet build -c Release transit/cs2-exec
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { resolve, join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { connect } from '../tools/gamectl/lib/index.js';
import { transitJobs } from './lib/plan.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
export const DLL = join(HERE, 'cs2-exec', 'bin', 'Release', 'net48', 'Cs1ConvTransit.dll');
const BUILDINGS_DLL = join(HERE, '..', 'buildings', 'bin', 'Release', 'net48', 'Cs2Buildings.dll');
const TYPE = 'Cs1ConvTransit.Ops';

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

export async function transit(argv) {
  const a = opts(argv);
  const cmd = a._[0];
  const only = a.only ? String(a.only).split(',').map(s => s.trim()) : undefined;
  const theme = a.theme || 'EU';
  if (cmd === 'plan') {
    const j = transitJobs(JSON.parse(readFileSync(a.plan, 'utf8')), { only, theme });
    return { stats: j.stats, skipped: j.skipped, lines: j.lines.map(l => ({ name: l.name, prefab: l.prefab, stops: l.stops.length, requiresStations: l.requiresStations })) };
  }
  if (!existsSync(DLL)) throw new Error('exec library not built: dotnet build -c Release transit/cs2-exec');
  const b = await connect('cs2');
  const op = (args, o = {}) => b.exec(DLL, { type: TYPE, args, timeout: 600, ...o });
  let out;
  switch (cmd) {
    case 'prefabs': out = await op({ op: 'prefabs' }); break;
    case 'list': out = { lines: await op({ op: 'lines.list' }) }; break;
    case 'stops': out = { stops: await op({ op: 'stops.list', type: a.type }) }; break;
    case 'counts': out = await op({ op: 'counts' }); break;
    case 'apply': {
      if (!a.plan) throw new Error('apply needs --plan');
      const j = transitJobs(JSON.parse(readFileSync(a.plan, 'utf8')), { only, theme });
      out = { plan: j.stats, planSkipped: j.skipped };
      if (!j.lines.length) { out.note = 'plan has no transit lines'; break; }
      // 1. kerbside stops (permanent object definitions attached to the road edges), in batches
      const created = [];
      // small batches with a pause between them: all 145 Skibidi stops + all lines at once crashed CS2 natively,
      // while 3 stops / one line per call worked (transit/tools/diag.mjs)
      for (let i = 0; i < j.stops.length; i += 20) {
        const r = await op({ op: 'stops.create', stops: j.stops.slice(i, i + 20) }, { phase: 'tool' });
        created.push(...r.results);
        await b.wait(10);
      }
      await b.wait(30);
      // the stops' road edges re-select their composition now (bus bay), not only on the next load
      out.touched = await op({ op: 'stops.touch' }, { phase: 'tool' });
      await b.wait(30);
      out.stops = { requested: j.stops.length, defined: created.filter(r => !r.error).length, errors: created.filter(r => r.error) };
      // resolve each line stop at the exact position its stop object was created (up to 14 m off the centre line on
      // wide roads), with a tight radius, so neighbouring stops on the other side are never picked
      const posByKey = new Map(created.filter(r => r.pos).map(r => [r.key, r.pos]));
      for (const l of j.lines) for (const s of l.stops) { const p = s.key && posByKey.get(s.key); if (p) { s.x = p[0]; s.z = p[2]; } }
      // 2. lines: route definitions + one apply frame (phase + pump tool)
      const lines = { created: 0, skipped: 0, tempRoutes: 0, applied: true, lines: [] };
      for (const l of j.lines) { // one line per call
        const r = await op({ op: 'lines.create', lines: [l], frames: 40, radius: 4 }, { phase: 'tool', pump: 'tool' });
        lines.created += r.created; lines.skipped += r.skipped; lines.tempRoutes += r.tempRoutes; if (r.created) lines.applied &&= r.applied; lines.lines.push(...r.lines);
      }
      out.lines = { created: lines.created, skipped: lines.skipped, tempRoutes: lines.tempRoutes, applied: lines.applied, detail: lines.lines };
      // 3. names and CS1 numbers (main phase: NameSystem needs a command buffer)
      const byKey = new Map(j.lines.map(l => [l.key, l]));
      const routes = lines.lines.filter(l => l.route).map(l => ({ route: l.route, name: byKey.get(l.key)?.name, number: byKey.get(l.key)?.number }));
      out.lines.named = (await op({ op: 'lines.name', routes })).named;
      await b.wait(60);
      out.check = { lines: await op({ op: 'lines.list' }), counts: await op({ op: 'counts' }) };
      break;
    }
    case 'depots': {
      // docs/DECISIONS.md #83: metro/train yards only at the crop edge where the line's track leaves, linked to it
      if (!a.plan) throw new Error('depots needs --plan');
      const plan = JSON.parse(readFileSync(a.plan, 'utf8'));
      const lines = (plan.transit?.lines ?? []).map(l => ({ name: l.name, cs1Type: l.cs1Type, stops: (l.stops ?? []).map(s => ({ x: s.x, z: s.z })) }));
      // --interior-yards (opt-in, DECISIONS-transit #14): a type whose track doesn't leave the crop gets a yard beside
      // an open end of its track inside the area instead of a "place it yourself" report entry
      const interior = !!(a['interior-yards'] || a.interiorYards);
      const depotMode = interior ? Object.fromEntries(['Metro', 'Train'].map(t => [t, plan.transit?.depotMode?.[t] === 'manual' ? 'manual' : 'interior'])) : (plan.transit?.depotMode ?? null);
      const yp = await op({ op: 'yards.plan', bounds: plan.bounds, lines, depotMode, edgeMargin: Number(a.edgeMargin ?? 80) });
      out = { types: yp.types, report: yp.report, items: yp.buildings.items };
      // train/metro lines pathfind when they are created; created before their stations were joined to the track
      // they keep no path, so --recreate deletes and re-creates them afterwards
      const recreateRailLines = async (planFile) => {
        const del = await op({ op: 'lines.delete', types: ['Train', 'Subway'] }, { phase: 'tool' });
        await b.wait(30);
        const names = (del.deleted ?? []).filter(Boolean);
        if (!names.length) return { deleted: [] };
        const r = await transit(['apply', '--plan', planFile, '--only', names.join(',')]);
        return { deleted: names, created: r.lines?.created, skipped: r.lines?.skipped };
      };
      if (!yp.buildings.items.length) {
        // no yard to place: still join the stations' platform tracks to the line track, and recompute the paths
        out.link = await op({ op: 'tracklink', maxDistance: Number(a.maxDistance ?? 2500), ...(interior ? { maxGrade: 0.06, yardLinkPrefab: 'yard', touchEnds: !!a.touchEnds, tangentLinks: !a.noTangentLinks } : {}) }, { phase: 'tool', pump: 'tool' });
        await b.wait(60);
        out.repath = await op({ op: 'lines.repath', types: ['Train', 'Subway'] }, { phase: 'tool' });
        await b.wait(120);
        if (a.recreate) out.recreate = await recreateRailLines(a.plan);
        break;
      }
      if (!existsSync(BUILDINGS_DLL)) { out.error = 'buildings placer not built: dotnet build -c Release buildings'; break; }
      const dir = resolve(a.workDir ?? join(HERE, '..', '.gamectl', 'transit'));
      mkdirSync(dir, { recursive: true });
      const itemsFile = join(dir, 'edge-yards.json');
      writeFileSync(itemsFile, JSON.stringify({ buildings: yp.buildings }, null, 1));
      const placed = await b.exec(BUILDINGS_DLL, { phase: 'tool', pump: 'tool', timeout: 600,
        args: { op: 'place', plan: itemsFile, offsetX: 0, offsetZ: 0, fallback: 'nearby', nearbyRadius: Number(a.nearbyRadius ?? 600), report: join(dir, 'edge-yards-report.json') } });
      out.placed = { placed: placed.placed, failed: placed.failed, sample: placed.sample };
      // a yard the placer could not fit (no free land with road access within reach): the line needs a manual depot
      for (const r of (placed.sample ?? []).filter(r => r.status !== 'placed' && r.status !== 'alreadyPlaced')) {
        const item = yp.buildings.items.find(i => i.id === r.id) ?? {};
        const kind = /Subway/.test(item.prefab ?? '') ? 'subway yard' : 'rail yard';
        for (const n of item.lines ?? []) out.report.push({ level: 'warning', kind: 'depot-noroom', line: n,
          message: `${n}: no free land with road access for a ${kind} within ${item.maxDistance ?? 2500} m of where its track leaves the converted area, so no depot was placed: place a ${kind} yourself and connect its track to the line.`, reason: r.reason });
      }
      if (!placed.placed) break;
      await b.wait(60);
      out.link = await op({ op: 'tracklink', maxDistance: Number(a.maxDistance ?? 2500), ...(interior ? { maxGrade: 0.06, yardLinkPrefab: 'yard', touchEnds: !!a.touchEnds, tangentLinks: !a.noTangentLinks } : {}) }, { phase: 'tool', pump: 'tool' });
      await b.wait(60);
      // the lines' paths were computed before their stations and yard were joined to the track: recompute them
      out.repath = await op({ op: 'lines.repath', types: ['Train', 'Subway'] }, { phase: 'tool' });
      await b.wait(120);
      if (a.recreate) out.recreate = await recreateRailLines(a.plan);
      out.yards = (await op({ op: 'yards.inspect' })).yards;
      break;
    }
    default: throw new Error('unknown command ' + cmd + ' (apply, depots, plan, prefabs, list, stops, counts)');
  }
  if (a.out) writeFileSync(resolve(a.out), JSON.stringify(out, null, 2) + '\n');
  return out;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  transit(process.argv.slice(2)).then(o => console.log(JSON.stringify(o, null, 2)), e => { console.error('error: ' + (e.stack || e.message)); process.exitCode = 1; });
}
