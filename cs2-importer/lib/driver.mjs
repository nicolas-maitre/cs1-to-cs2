// Shared helpers for scripts that drive the CS2 importer: game locks, the importer DLL build, screenshots, the report
// summary. Used by cs2-importer/run.mjs and tools/e2e/run.mjs.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
export const IMPORTER_DIR = path.resolve(here, '..');
export const REPO = path.resolve(IMPORTER_DIR, '..');
export const IMPORTER_DLL = path.join(IMPORTER_DIR, 'bin', 'Release', 'net48', 'Cs2Importer.dll');
export const LOCAL_DIR = path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(), 'AppData', 'Local'), 'cs1-to-cs2');
export const CS2_MODSDATA_RB = path.join(os.homedir(), 'AppData', 'LocalLow', 'Colossal Order', 'Cities Skylines II', 'ModsData', 'RoadBuilder');

/** dotnet build -c Release of the importer; returns the DLL path. */
export function buildImporter() {
  execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q'], { cwd: IMPORTER_DIR, stdio: ['ignore', 'ignore', 'inherit'] });
  return IMPORTER_DLL;
}

export function planPath(p) {
  if (!p) throw new Error('missing plan path');
  const abs = path.resolve(p);
  return fs.statSync(abs).isDirectory() ? path.join(abs, 'cs1conv-plan.json') : abs;
}

export function readPlan(p) { return JSON.parse(fs.readFileSync(planPath(p), 'utf8')); }

// ------------------------------------------------------------------ game locks

/** %LOCALAPPDATA%\cs1-to-cs2\<game>.lock */
export function lockPath(game) { return path.join(LOCAL_DIR, `${game}.lock`); }

export function readLock(game) {
  try { return JSON.parse(fs.readFileSync(lockPath(game), 'utf8')); } catch { return null; }
}

/** Owner and start time of a lock in any of the formats in use ({owner, time} or {session, since}). */
export function lockInfo(l) {
  const when = l?.time ?? l?.since;
  return { who: l?.owner ?? l?.session ?? 'unknown', when, ageH: when ? (Date.now() - Date.parse(when)) / 3600e3 : NaN };
}

/**
 * Takes the game lock for `owner`. Refuses while another owner's lock is under `maxAgeH` hours old; a stale one is
 * taken over. Re-taking our own lock refreshes it.
 */
export function takeLock(game, owner, session, { maxAgeH = 2, log = () => {} } = {}) {
  const l = readLock(game);
  if (l && (l.owner ?? l.session) !== owner) {
    const { who, when, ageH } = lockInfo(l);
    if (!(ageH >= maxAgeH)) {
      const e = new Error(`${game.toUpperCase()} is locked by ${who} since ${when} (${Number.isFinite(ageH) ? ageH.toFixed(2) + ' h' : 'unknown age'}): wait and retry`);
      e.code = 'locked';
      throw e;
    }
    log(`taking over a stale ${game} lock from ${who} (${ageH.toFixed(1)} h old)`);
  }
  fs.mkdirSync(LOCAL_DIR, { recursive: true });
  const now = new Date().toISOString();
  fs.writeFileSync(lockPath(game), JSON.stringify({ owner, session, since: now, time: now, pid: process.pid }) + '\n');
}

export function releaseLock(game, owner) {
  const l = readLock(game);
  if (l && (l.owner ?? l.session) === owner) fs.rmSync(lockPath(game));
}

/** Waits (polling every 15 s) until the lock is free or ours, up to `timeoutMin`, then takes it. */
export async function waitAndTakeLock(game, owner, session, { timeoutMin = 60, log = () => {} } = {}) {
  const until = Date.now() + timeoutMin * 60e3;
  for (;;) {
    try { takeLock(game, owner, session, { log }); return; } catch (e) {
      if (e.code !== 'locked' || Date.now() > until) throw e;
      log(`${e.message}; waiting`);
      await new Promise((r) => setTimeout(r, 15000));
    }
  }
}

// ------------------------------------------------------------------ report + screenshots

export function summary(r) {
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

export function planBounds(plan, offsetX = 0, offsetZ = 0) {
  const xs = plan.nodes.map((n) => n.x + offsetX), zs = plan.nodes.map((n) => n.z + offsetZ);
  const minX = Math.min(...xs), maxX = Math.max(...xs), minZ = Math.min(...zs), maxZ = Math.max(...zs);
  return { minX, maxX, minZ, maxZ, cx: (minX + maxX) / 2, cz: (minZ + maxZ) / 2, size: Math.max(maxX - minX, maxZ - minZ), ox: offsetX, oz: offsetZ };
}

/**
 * Overview, oblique and close-ups (an elevated segment, a tunnel/trench, the busiest junction) of a plan's area.
 * Close-up camera heights follow the node, so they work on real terrain too.
 */
export async function planShots(bridge, plan, { dir, tag, offsetX = 0, offsetZ = 0, log = () => {} }) {
  const bb = planBounds(plan, offsetX, offsetZ);
  fs.mkdirSync(dir, { recursive: true });
  const views = [
    { name: 'overview', x: bb.cx, z: bb.cz, distance: Math.max(400, bb.size * 1.1), yaw: 0, pitch: 75 },
    { name: 'oblique', x: bb.cx, z: bb.cz, distance: Math.max(300, bb.size * 0.8), yaw: 30, pitch: 40 },
  ];
  const node = (id) => plan.nodes.find((x) => x.id === id);
  const elevated = plan.segments.filter((s) => (s.elevationStart ?? 0) > 1 || (s.elevationEnd ?? 0) > 1);
  if (elevated.length) {
    const n = node(elevated[Math.floor(elevated.length / 2)].start);
    if (n) views.push({ name: 'elevated', x: n.x + bb.ox, z: n.z + bb.oz, distance: 250, yaw: 60, pitch: 30 });
  }
  const low = plan.nodes.filter((n) => (n.heightAboveTerrain ?? 0) <= -8);
  const tunnel = plan.segments.filter((s) => (s.elevationStart ?? 0) < -1 || (s.elevationEnd ?? 0) < -1);
  const lowNode = tunnel.length ? node(tunnel[0].start) : low[Math.floor(low.length / 2)];
  if (lowNode) views.push({ name: 'tunnel', x: lowNode.x + bb.ox, z: lowNode.z + bb.oz, distance: 250, yaw: 120, pitch: 35 });
  const deg = new Map();
  for (const s of plan.segments) for (const id of [s.start, s.end]) deg.set(id, (deg.get(id) ?? 0) + 1);
  const busiest = [...deg.entries()].sort((a, c) => c[1] - a[1])[0];
  if (busiest) {
    const n = node(busiest[0]);
    views.push({ name: 'junction', x: n.x + bb.ox, z: n.z + bb.oz, distance: 180, yaw: 20, pitch: 55 });
  }
  const files = [];
  for (const v of views) {
    await bridge.cameraSet({ x: v.x, z: v.z, distance: v.distance, yaw: v.yaw, pitch: v.pitch });
    await bridge.wait(45, 1.5);
    const f = path.join(dir, `${tag}-${v.name}.png`);
    await bridge.screenshot(f);
    files.push(f);
    log('shot', f);
  }
  return files;
}
