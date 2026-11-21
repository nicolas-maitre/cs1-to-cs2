#!/usr/bin/env node
// Drives the M1 CS2 importer in a running CS2 through tools/gamectl (Cs2Bridge exec). Run from anywhere:
//
//   node cs2-importer/run.mjs start                      lock CS2 (%LOCALAPPDATA%\cs1-to-cs2\cs2.lock) and start it
//   node cs2-importer/run.mjs new [--map "<name|id>"] [--name cs1conv-city]
//   node cs2-importer/run.mjs check <plan dir|json>
//   node cs2-importer/run.mjs import <plan dir|json> [--clear] [--batch 400] [--tag name]
//   node cs2-importer/run.mjs verify <plan dir|json>
//   node cs2-importer/run.mjs shots <plan dir|json> [--tag name]   overview + close-ups of the plan's area
//   node cs2-importer/run.mjs save cs1conv-<name>
//   node cs2-importer/run.mjs stop                       quit CS2 and release the lock
//   node cs2-importer/run.mjs copy-rb <plan dir>         copy roadbuilder/*-cs1conv.json to ModsData (CS2 must be closed)
//
// Reports and screenshots go to <repo>/.gamectl/import/ (git-ignored). The importer DLL is built on demand
// (dotnet build -c Release); exec loads a fresh copy on every call, so a rebuild needs no game restart.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { connect, start, stop, status } from '../tools/gamectl/lib/index.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..');
const outDir = path.join(repo, '.gamectl', 'import');
const dll = path.join(here, 'bin', 'Release', 'net48', 'Cs2Importer.dll');
const lockFile = path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(), 'AppData', 'Local'), 'cs1-to-cs2', 'cs2.lock');
const modsDataRb = path.join(os.homedir(), 'AppData', 'LocalLow', 'Colossal Order', 'Cities Skylines II', 'ModsData', 'RoadBuilder');
const LOCK_OWNER = 'cs2-importer';
// M1 test placement (DECISIONS-importer.md): Corral Riches, plan shifted to the flattest dry 3.2 km square of the map
const DEFAULT_MAP = process.env.CS1CONV_MAP ?? 'Corral Riches';
const DEFAULT_OFFSET = process.env.CS1CONV_OFFSET ?? '2000,-4000';

const argv = process.argv.slice(2);
const cmd = argv[0];
const VALUE_FLAGS = new Set(['--map', '--name', '--batch', '--tag', '--offset']);
const pos = argv.slice(1).filter((a, i, all) => !a.startsWith('--') && !(i > 0 && VALUE_FLAGS.has(all[i - 1])));
const opt = (name, def) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : def; };
const flag = (name) => argv.includes(name);

function log(...a) { console.error('[run]', ...a); }

/** --offset x,z (default DEFAULT_OFFSET; "0,0" = the plan's own coordinates) */
function offset() {
  const [x, z] = String(opt('--offset', DEFAULT_OFFSET)).split(',').map(Number);
  if (!Number.isFinite(x) || !Number.isFinite(z)) throw new Error('--offset must be x,z');
  return { offsetX: x, offsetZ: z };
}

function planPath(p) {
  if (!p) throw new Error('missing plan path');
  const abs = path.resolve(p);
  return fs.statSync(abs).isDirectory() ? path.join(abs, 'cs1conv-plan.json') : abs;
}

function readPlan(p) { return JSON.parse(fs.readFileSync(planPath(p), 'utf8')); }

function buildDll() {
  execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q'], { cwd: here, stdio: ['ignore', 'ignore', 'inherit'] });
  return dll;
}

// ------------------------------------------------------------------ lock

function readLock() {
  try { return JSON.parse(fs.readFileSync(lockFile, 'utf8')); } catch { return null; }
}

function takeLock() {
  const l = readLock();
  if (l && l.owner !== LOCK_OWNER) {
    // other workers write {session, since}; ours is {owner, time}
    const who = l.owner ?? l.session ?? 'unknown', when = l.time ?? l.since;
    const ageH = (Date.now() - Date.parse(when)) / 3600e3;
    if (!(ageH >= 2)) throw new Error(`CS2 is locked by ${who} since ${when} (${Number.isFinite(ageH) ? ageH.toFixed(2) + ' h' : 'unknown age'}): wait and retry`);
    log(`taking over a stale lock from ${who} (${ageH.toFixed(1)} h old)`);
  }
  fs.mkdirSync(path.dirname(lockFile), { recursive: true });
  const now = new Date().toISOString();
  fs.writeFileSync(lockFile, JSON.stringify({ owner: LOCK_OWNER, session: 'cs2-importer worker (M1: cs2-importer/, tools/cs2-bridge/)', since: now, time: now, pid: process.pid }) + '\n');
}

function releaseLock() {
  const l = readLock();
  if (l && l.owner === LOCK_OWNER) fs.rmSync(lockFile);
}

// ------------------------------------------------------------------ helpers

async function bridge() { return connect('cs2'); }

function bounds(plan) {
  const { offsetX, offsetZ } = offset();
  const xs = plan.nodes.map((n) => n.x + offsetX), zs = plan.nodes.map((n) => n.z + offsetZ);
  const minX = Math.min(...xs), maxX = Math.max(...xs), minZ = Math.min(...zs), maxZ = Math.max(...zs);
  return { minX, maxX, minZ, maxZ, cx: (minX + maxX) / 2, cz: (minZ + maxZ) / 2, size: Math.max(maxX - minX, maxZ - minZ), ox: offsetX, oz: offsetZ };
}

function summary(r) {
  const res = r.result ?? r;
  const lines = [
    `planned ${res.segments_planned} segments, attempted ${res.segments_attempted}, built ${res.segments_built}, failed ${res.segments_failed}`,
    `nodes ${res.nodes_found}/${res.nodes_planned}; CS2 edges ${res.cs2_edges_for_plan}; elevated ${res.elevated_built}/${res.elevated_planned}; tunnel ${res.tunnel_built}/${res.tunnel_planned}; other edges in area ${res.other_cs2_edges_in_area}`,
  ];
  if (r.failures_by_reason) lines.push(`failures: ${JSON.stringify(r.failures_by_reason)}`);
  if (r.plan?.skipped_by_reason) lines.push(`skipped in plan: ${JSON.stringify(r.plan.skipped_by_reason)}`);
  if (r.temp_after ?? r.temp) lines.push(`temp: ${JSON.stringify(r.temp_after ?? r.temp)}`);
  if (r.seconds !== undefined) lines.push(`${r.seconds} s, ${r.batches?.length ?? 0} batches`);
  return lines.join('\n');
}

async function shots(planArg, tag) {
  const plan = readPlan(planArg);
  const b = await bridge();
  const bb = bounds(plan);
  fs.mkdirSync(outDir, { recursive: true });
  const files = [];
  const views = [
    { name: 'overview', x: bb.cx, z: bb.cz, distance: Math.max(400, bb.size * 1.1), yaw: 0, pitch: 75 },
    { name: 'oblique', x: bb.cx, z: bb.cz, distance: Math.max(300, bb.size * 0.8), yaw: 30, pitch: 40 },
  ];
  // close-ups: elevated / tunnel segments, plus the busiest node
  const elevated = plan.segments.filter((s) => (s.elevationStart ?? 0) > 1 || (s.elevationEnd ?? 0) > 1);
  if (elevated.length) {
    const n = plan.nodes.find((x) => x.id === elevated[Math.floor(elevated.length / 2)].start);
    if (n) views.push({ name: 'elevated', x: n.x + bb.ox, z: n.z + bb.oz, distance: 250, yaw: 60, pitch: 30 });
  }
  const tunnel = plan.segments.filter((s) => (s.elevationStart ?? 0) < -1 || (s.elevationEnd ?? 0) < -1);
  if (tunnel.length) {
    const n = plan.nodes.find((x) => x.id === tunnel[0].start);
    if (n) views.push({ name: 'tunnel', x: n.x + bb.ox, z: n.z + bb.oz, distance: 250, yaw: 120, pitch: 35 });
  }
  const deg = new Map();
  for (const s of plan.segments) for (const id of [s.start, s.end]) deg.set(id, (deg.get(id) ?? 0) + 1);
  const busiest = [...deg.entries()].sort((a, c) => c[1] - a[1])[0];
  if (busiest) {
    const n = plan.nodes.find((x) => x.id === busiest[0]);
    views.push({ name: 'junction', x: n.x + bb.ox, z: n.z + bb.oz, distance: 180, yaw: 20, pitch: 55 });
  }
  for (const v of views) {
    await b.cameraSet({ x: v.x, z: v.z, distance: v.distance, yaw: v.yaw, pitch: v.pitch });
    await b.wait(45, 1.5);
    const f = path.join(outDir, `${tag}-${v.name}.png`);
    await b.screenshot(f);
    files.push(f);
    log('shot', f);
  }
  return files;
}

// ------------------------------------------------------------------ commands

async function main() {
  switch (cmd) {
    case 'start': {
      const st = await status('cs2');
      if (st.running && readLock()?.owner !== LOCK_OWNER) throw new Error('CS2 is already running and not locked by us');
      takeLock();
      const r = await start('cs2', {});
      log(`cs2 ready (pid ${r.pid})`);
      return r;
    }
    case 'stop': {
      try { await stop('cs2', {}); } finally { releaseLock(); }
      return { stopped: true };
    }
    case 'new': {
      const b = await bridge();
      return b.newGame(opt('--map', DEFAULT_MAP), { name: opt('--name', 'cs1conv-m1') });
    }
    case 'maps': return (await bridge()).maps();
    case 'save': {
      if (!pos[0]) throw new Error('usage: save cs1conv-<name>');
      return (await bridge()).saveWrite(pos[0]);
    }
    case 'check': {
      const b = await bridge();
      return b.exec(buildDll(), { args: { op: 'check', plan: planPath(pos[0]), ...offset() }, timeout: 300 });
    }
    case 'verify': {
      const b = await bridge();
      const r = await b.exec(buildDll(), { args: { op: 'verify', plan: planPath(pos[0]), ...offset() }, timeout: 300 });
      log('\n' + summary(r));
      return r;
    }
    case 'clear': {
      const b = await bridge();
      return b.exec(buildDll(), { phase: 'tool', pump: 'tool', args: { op: 'clear', plan: planPath(pos[0]), ...offset() }, timeout: 300 });
    }
    case 'import': {
      const b = await bridge();
      const tag = opt('--tag', path.basename(path.dirname(planPath(pos[0]))));
      fs.mkdirSync(outDir, { recursive: true });
      const report = path.join(outDir, `${tag}-report.json`);
      const args = { op: 'import', plan: planPath(pos[0]), clear: flag('--clear'), report, ...offset() };
      if (opt('--batch')) args.batchSize = Number(opt('--batch'));
      await b.pause().catch(() => {});
      const r = await b.exec(buildDll(), { phase: 'tool', pump: 'tool', args, timeout: 3600 });
      log(`report: ${report}\n${summary(r)}`);
      return r;
    }
    case 'shots': return shots(pos[0], opt('--tag', path.basename(path.dirname(planPath(pos[0])))));
    case 'copy-rb': {
      const st = await status('cs2');
      if (st.running) throw new Error('CS2 is running: Road Builder only reads configs at startup, copy before starting it');
      const dir = path.join(path.resolve(pos[0]), 'roadbuilder');
      fs.mkdirSync(modsDataRb, { recursive: true });
      const copied = [];
      for (const f of fs.readdirSync(dir)) {
        if (!f.endsWith('-cs1conv.json')) continue; // only ever our own generated configs
        fs.copyFileSync(path.join(dir, f), path.join(modsDataRb, f));
        copied.push(f);
      }
      return { copied, to: modsDataRb };
    }
    default:
      console.error(fs.readFileSync(fileURLToPath(import.meta.url), 'utf8').split('\n').slice(1, 14).join('\n'));
      process.exitCode = 2;
      return null;
  }
}

main().then((r) => { if (r != null) console.log(JSON.stringify(r, null, 1)); }).catch((e) => { console.error(`[run] ${e.stack ?? e}`); process.exitCode = 1; });
