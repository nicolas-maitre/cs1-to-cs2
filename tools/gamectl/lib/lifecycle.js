// start / stop / status of a game process and its bridge.

import fs from 'node:fs';
import { GamectlError } from './errors.js';
import { gameConfig } from './config.js';
import { defaultDeps } from './proc.js';
import { Bridge, connect, health, readDiscoveryRaw } from './bridge.js';
import { LogCursor, snapshot } from './logs.js';

/** @typedef {import('./config.js').GameId} GameId */
/** @typedef {import('./config.js').GameConfig} GameConfig */
/** @typedef {import('./proc.js').ProcDeps} ProcDeps */

/**
 * @typedef {object} LifecycleOpts
 * @property {Partial<GameConfig>} [config]  overrides for gameConfig() (tests: exe, args, markerFile, bridgeDir…)
 * @property {Partial<ProcDeps>} [deps]      overrides for process helpers (tests: isSteamRunning, listPids…)
 * @property {(msg: string) => void} [onProgress]
 */

/** Split a flags string like `-a "b c" d` into args. @param {string | string[] | undefined} flags */
export function splitFlags(flags) {
  if (flags === undefined) return [];
  if (Array.isArray(flags)) return flags;
  /** @type {string[]} */
  const out = [];
  for (const m of flags.matchAll(/"([^"]*)"|'([^']*)'|(\S+)/g)) out.push(m[1] ?? m[2] ?? m[3]);
  return out;
}

/** @param {LifecycleOpts} opts @returns {ProcDeps} */
const depsOf = (opts) => ({ ...defaultDeps, ...(opts.deps ?? {}) });

/**
 * Try to connect to an already running bridge; null if there is none.
 * @param {GameConfig} cfg @param {ProcDeps} deps
 */
async function tryConnect(cfg, deps) {
  try {
    return await connect(cfg.game, { dir: cfg.bridgeDir, isAlive: deps.isAlive });
  } catch (e) {
    if (e instanceof GamectlError && e.code !== 'auth') return null;
    throw e;
  }
}

/**
 * A discovery file written for one of `pids` at/after `notBefore`, whose /health answers with a first status.
 * @param {GameConfig} cfg @param {Set<number>} pids @param {number} notBefore
 * @returns {Promise<{bridge: Bridge | null, why: string}>}
 */
async function probeBridge(cfg, pids, notBefore) {
  let raw;
  try {
    raw = readDiscoveryRaw(cfg.game, { dir: cfg.bridgeDir });
  } catch (e) {
    return { bridge: null, why: /** @type {Error} */ (e).message }; // half-written file: retry
  }
  if (!raw) return { bridge: null, why: 'no discovery file yet' };
  if (!pids.has(raw.pid)) return { bridge: null, why: `discovery file is for pid ${raw.pid}, not the game (${[...pids].join(', ') || 'none'})` };
  if (raw.mtimeMs < notBefore) return { bridge: null, why: 'discovery file predates the launch' };
  try {
    const h = await health(raw.port, { timeoutMs: 2000 });
    if (h.pid !== undefined && h.pid !== raw.pid) return { bridge: null, why: `port ${raw.port} answers for pid ${h.pid}` };
    if (h.status == null) return { bridge: null, why: 'bridge up, waiting for its first status' };
    return { bridge: new Bridge(raw), why: '' };
  } catch (e) {
    return { bridge: null, why: /** @type {Error} */ (e).message };
  }
}

/**
 * Start the game (or reuse a running one) and wait until its bridge is ready; optionally load a save.
 *
 * Readiness: the ready marker must appear in content written to the marker log AFTER the spawn (a LogCursor from a
 * pre-spawn snapshot; a recreated/truncated log is read from the start), then a discovery file whose pid is the new
 * process and whose mtime is after the spawn, and /health must answer with that pid and a non-null status.
 *
 * @param {GameId} game
 * @param {LifecycleOpts & {load?: string, flags?: string | string[], timeout?: number, loadTimeout?: number, relaunchGraceS?: number}} [opts]
 *   timeout: seconds for the game to become ready (default 300); loadTimeout: seconds for --load (default 900)
 */
export async function start(game, opts = {}) {
  const cfg = gameConfig(game, opts.config);
  const deps = depsOf(opts);
  const progress = opts.onProgress ?? (() => {});
  const t0 = Date.now();
  const deadline = t0 + (opts.timeout ?? 300) * 1000;

  if (!(await deps.isSteamRunning())) {
    throw new GamectlError('lifecycle', 'Steam is not running: start Steam first (the game is launched directly with SteamAppId and needs it)');
  }

  /** @type {Bridge | null} */
  let bridge = await tryConnect(cfg, deps);
  let reused = !!bridge;
  let spawned = false;

  if (!bridge) {
    const running = await deps.listPids(cfg.image);
    if (running.length) {
      progress(`${cfg.image} already running (pid ${running.join(', ')}); waiting for its bridge`);
      bridge = await waitBridge(cfg, deps, new Set(running), 0, deadline, null);
      reused = true;
    } else {
      bridge = await spawnAndWait(cfg, deps, splitFlags(opts.flags), deadline, progress, (opts.relaunchGraceS ?? 30) * 1000);
      spawned = true;
    }
  } else {
    progress(`reusing running ${game} (pid ${bridge.pid}, port ${bridge.port})`);
  }

  /** @type {Record<string, unknown>} */
  const out = { game, pid: bridge.pid, port: bridge.port, reused, spawned, ready_s: (Date.now() - t0) / 1000 };
  if (opts.load !== undefined) {
    progress(`loading save "${opts.load}"`);
    out.loaded = await loadAndWait(bridge, opts.load, opts.loadTimeout ?? 900);
  }
  out.elapsed_s = (Date.now() - t0) / 1000;
  return { bridge, ...out };
}

/**
 * @param {GameConfig} cfg @param {ProcDeps} deps @param {string[]} flags @param {number} deadline
 * @param {(msg: string) => void} progress @param {number} relaunchGraceMs
 */
async function spawnAndWait(cfg, deps, flags, deadline, progress, relaunchGraceMs) {
  if (!cfg.exe) throw new GamectlError('lifecycle', `${cfg.game} install folder unknown: set ${cfg.game.toUpperCase()}_DIR or ${cfg.game === 'cs1' ? 'CS1Managed' : 'CS2Managed'} in local.props`);
  if (!fs.existsSync(cfg.exe)) throw new GamectlError('lifecycle', `game exe not found: ${cfg.exe}`);
  if (!cfg.markerFile) throw new GamectlError('lifecycle', `no ready-marker log configured for ${cfg.game}`);

  const markerSince = snapshot(cfg.markerFile); // BEFORE spawning: anything already in the log is from an older run
  const before = new Set(await deps.listPids(cfg.image));
  const spawnTime = Date.now();
  // CS1TOCS2_GAMECTL=1 tells the CS2 bridge this game is script-driven: it blocks autosaves (in memory only) so tests
  // never rotate out the user's autosaves. See tools/cs2-bridge/AutosaveGuard.cs.
  const env = { ...process.env, SteamAppId: cfg.appId, SteamGameId: cfg.appId, CS1TOCS2_GAMECTL: '1' };
  const args = [...cfg.args, ...flags];
  const child = deps.spawn(cfg.exe, args, { env, cwd: cfg.installDir ?? undefined });
  progress(`spawned ${cfg.exe} ${args.join(' ')} (pid ${child.pid})`);
  return waitBridge(cfg, deps, new Set([child.pid]), spawnTime - 1000, deadline, { cursor: new LogCursor(cfg.markerFile, markerSince), child, before, progress, relaunchGraceMs });
}

/**
 * Poll until the bridge of one of `pids` is ready. With `launch`, first wait for the new ready marker and track the
 * spawned process (if it exits, a new process with the game's image name that wasn't running before is accepted —
 * covers a Steam relaunch — for relaunchGraceMs, default 30 s).
 * @param {GameConfig} cfg @param {ProcDeps} deps @param {Set<number>} pids @param {number} notBefore @param {number} deadline
 * @param {{cursor: LogCursor, child: import('./proc.js').SpawnedProcess, before: Set<number>, progress: (m: string) => void, relaunchGraceMs: number} | null} launch
 */
async function waitBridge(cfg, deps, pids, notBefore, deadline, launch) {
  let markerSeen = !launch;
  let why = 'waiting';
  let exitedAt = 0;
  let lastScan = 0;
  for (;;) {
    if (launch && !markerSeen) {
      const lines = launch.cursor.read();
      if (lines.some((l) => cfg.markerRe.test(l)) || cfg.markerRe.test(launch.cursor.pending())) {
        markerSeen = true;
        launch.progress(`ready marker ${cfg.markerRe} seen in ${cfg.markerFile}`);
      } else {
        why = `ready marker ${cfg.markerRe} not yet written to ${cfg.markerFile} by the new process`;
      }
    }
    if (launch) {
      const exit = launch.child.exited();
      if (exit) {
        pids.delete(launch.child.pid);
        if (!exitedAt) exitedAt = Date.now();
        if (Date.now() - lastScan > 2000) {
          lastScan = Date.now();
          for (const p of await deps.listPids(cfg.image)) if (!launch.before.has(p)) pids.add(p);
        }
        if (pids.size === 0 && Date.now() - exitedAt > launch.relaunchGraceMs) {
          throw new GamectlError('lifecycle', `${cfg.image} exited during startup (code ${exit.code}${exit.signal ? `, ${exit.signal}` : ''}) and no new game process appeared`);
        }
      }
    } else if (![...pids].some((p) => deps.isAlive(p))) {
      throw new GamectlError('lifecycle', `${cfg.image} (pid ${[...pids].join(', ')}) exited while waiting for its bridge`);
    }
    if (markerSeen) {
      const r = await probeBridge(cfg, pids, notBefore);
      if (r.bridge) return r.bridge;
      why = `bridge not ready: ${r.why}`;
    }
    if (Date.now() >= deadline) {
      throw new GamectlError('lifecycle', `${cfg.game} not ready in time (${why}); the game was left running`);
    }
    await deps.sleep(250);
  }
}

/** @param {Bridge} bridge @param {string} name @param {number} timeoutS */
async function loadAndWait(bridge, name, timeoutS) {
  const deadline = Date.now() + timeoutS * 1000;
  const result = await bridge.load(name, { timeout: timeoutS });
  for (;;) {
    const st = /** @type {any} */ (await bridge.state());
    if (st?.city_loaded && !st.loading) return { ...result, state: st };
    if (Date.now() >= deadline) throw new GamectlError('timeout', `save.load returned but state.city_loaded is still false after ${timeoutS} s`);
    await new Promise((r) => setTimeout(r, 500));
  }
}

/**
 * Stop the game: rpc `quit`, wait up to `wait` s for the process to exit, then kill it. `force` kills right away.
 * @param {GameId} game
 * @param {LifecycleOpts & {force?: boolean, wait?: number}} [opts]
 */
export async function stop(game, opts = {}) {
  const cfg = gameConfig(game, opts.config);
  const deps = depsOf(opts);
  const progress = opts.onProgress ?? (() => {});
  const pids = new Set(await deps.listPids(cfg.image));
  let raw = null;
  try {
    raw = readDiscoveryRaw(game, { dir: cfg.bridgeDir });
  } catch { /* unreadable file: ignore */ }
  if (raw && deps.isAlive(raw.pid)) pids.add(raw.pid);
  const alive = () => [...pids].filter((p) => deps.isAlive(p));
  if (alive().length === 0) return { game, was_running: false, stopped: true, method: 'none' };

  let method = 'quit';
  /** @type {string | undefined} */
  let note;
  if (!opts.force) {
    const bridge = await tryConnect(cfg, deps).catch(() => null);
    if (bridge) {
      try {
        await bridge.quit();
        progress(`quit sent to pid ${bridge.pid}; waiting up to ${opts.wait ?? 60} s`);
      } catch (e) {
        note = `quit failed: ${/** @type {Error} */ (e).message}`;
      }
      const until = Date.now() + (opts.wait ?? 60) * 1000;
      while (alive().length && Date.now() < until) await deps.sleep(250);
      if (alive().length) note = note ?? `still running after ${opts.wait ?? 60} s`;
    } else {
      note = 'bridge not answering';
    }
  }
  if (alive().length) {
    method = 'kill';
    progress(`killing ${cfg.image} pid ${alive().join(', ')}${note ? ` (${note})` : ''}`);
    for (const p of alive()) await deps.kill(p);
    const until = Date.now() + 15000;
    while (alive().length && Date.now() < until) await deps.sleep(250);
    if (alive().length) throw new GamectlError('lifecycle', `pid ${alive().join(', ')} still running after kill`);
  }
  // On Windows a process counts as dead for process.kill(pid, 0) as soon as it has an exit code, but it stays in the
  // process list (holding files and the window) for a few more seconds while it is torn down. Wait for that too, so a
  // `start` right after `stop` never races the old instance.
  const goneBy = Date.now() + 30000;
  while (Date.now() < goneBy) {
    const listed = (await deps.listPids(cfg.image)).filter((p) => pids.has(p));
    if (listed.length === 0) break;
    await deps.sleep(250);
  }
  return { game, was_running: true, stopped: true, method, ...(note ? { note } : {}) };
}

/**
 * Running? pid, discovery, health snapshot. Never throws for "not running".
 * @param {GameId} game
 * @param {LifecycleOpts} [opts]
 */
export async function status(game, opts = {}) {
  const cfg = gameConfig(game, opts.config);
  const deps = depsOf(opts);
  const pids = await deps.listPids(cfg.image);
  /** @type {Record<string, any>} */
  const out = { game, running: pids.length > 0, pids, bridge: null, status: null };
  let raw = null;
  try {
    raw = readDiscoveryRaw(game, { dir: cfg.bridgeDir });
  } catch (e) {
    out.error = /** @type {Error} */ (e).message;
  }
  if (raw) {
    const alive = deps.isAlive(raw.pid);
    out.discovery = { file: raw.file, pid: raw.pid, port: raw.port, started: raw.started, stale: !alive };
    if (alive) {
      out.running = true;
      if (!pids.includes(raw.pid)) out.pids = [...pids, raw.pid];
      try {
        const h = await health(raw.port);
        out.bridge = { ok: !!h.ok, pid: h.pid, port: raw.port, bridge_version: h.bridge_version, protocol: h.protocol, status_age_ms: h.status_age_ms };
        out.status = h.status ?? null;
        if (h.pid !== undefined && h.pid !== raw.pid) out.error = `port ${raw.port} answers for pid ${h.pid}, discovery says ${raw.pid}`;
      } catch (e) {
        out.error = /** @type {Error} */ (e).message;
      }
    }
  }
  out.ready = !!(out.bridge?.ok && !out.error);
  return out;
}
