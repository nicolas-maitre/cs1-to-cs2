// Process helpers (Windows-first, no dependencies). Lifecycle functions take these as injectable `deps`.

import { execFile, spawn } from 'node:child_process';

/**
 * @typedef {object} SpawnedProcess
 * @property {number} pid
 * @property {() => ({code: number | null, signal: string | null} | null)} exited  exit info once it exited, else null
 */

/**
 * @typedef {object} ProcDeps
 * @property {() => Promise<boolean>} isSteamRunning
 * @property {(image: string) => Promise<number[]>} listPids   pids of running processes with that image name
 * @property {(pid: number) => boolean} isAlive
 * @property {(pid: number) => Promise<void>} kill             force-kill (tree)
 * @property {(exe: string, args: string[], opts: {env: Record<string, string | undefined>, cwd?: string}) => SpawnedProcess} spawn
 * @property {(ms: number) => Promise<void>} sleep
 */

/** @param {string} cmd @param {string[]} args @returns {Promise<string>} */
function run(cmd, args) {
  return new Promise((resolve, reject) => {
    execFile(cmd, args, { windowsHide: true, maxBuffer: 16 * 1024 * 1024 }, (err, stdout) => {
      if (err) reject(err);
      else resolve(stdout);
    });
  });
}

/** @param {string} image @returns {Promise<number[]>} */
export async function listPids(image) {
  if (process.platform !== 'win32') {
    try {
      const out = await run('pgrep', ['-f', image]);
      return out.split(/\s+/).filter(Boolean).map(Number);
    } catch {
      return [];
    }
  }
  const out = await run('tasklist', ['/FI', `IMAGENAME eq ${image}`, '/FO', 'CSV', '/NH']);
  /** @type {number[]} */
  const pids = [];
  for (const line of out.split(/\r?\n/)) {
    const cols = line.match(/"([^"]*)"/g)?.map((c) => c.slice(1, -1));
    if (cols && cols.length >= 2 && cols[0].toLowerCase() === image.toLowerCase()) pids.push(Number(cols[1]));
  }
  return pids;
}

/** @param {number} pid */
export function isAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch (e) {
    return /** @type {NodeJS.ErrnoException} */ (e).code === 'EPERM';
  }
}

/** @param {number} pid */
export async function kill(pid) {
  if (process.platform === 'win32') {
    try {
      await run('taskkill', ['/PID', String(pid), '/T', '/F']);
    } catch (e) {
      if (isAlive(pid)) throw e;
    }
  } else {
    try {
      process.kill(pid, 'SIGKILL');
    } catch { /* already gone */ }
  }
}

/** Spawn detached so the game outlives gamectl; stdio is ignored. */
/** @type {ProcDeps['spawn']} */
export function spawnDetached(exe, args, opts) {
  const child = spawn(exe, args, { detached: true, stdio: 'ignore', env: opts.env, cwd: opts.cwd, windowsHide: false });
  /** @type {{code: number | null, signal: string | null} | null} */
  let exit = null;
  /** @type {Error | null} */
  let spawnError = null;
  child.on('exit', (code, signal) => { exit = { code, signal }; });
  child.on('error', (e) => { spawnError = e; exit = { code: -1, signal: null }; });
  child.unref();
  if (child.pid === undefined) {
    // spawn failures (ENOENT, EACCES) surface asynchronously; report a dead process
    return { pid: -1, exited: () => exit ?? { code: -1, signal: spawnError ? String(spawnError) : null } };
  }
  return { pid: child.pid, exited: () => exit };
}

/** @param {number} ms */
export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** @type {ProcDeps} */
export const defaultDeps = {
  isSteamRunning: async () => (await listPids(process.platform === 'win32' ? 'steam.exe' : 'steam')).length > 0,
  listPids,
  isAlive,
  kill,
  spawn: spawnDetached,
  sleep,
};
