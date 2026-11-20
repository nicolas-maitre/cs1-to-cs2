// Shared test helpers (not a test file: no .test.js suffix).
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { main } from '../lib/cli.js';

export const FAKE_BRIDGE = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fake-bridge.js');

/** @param {string} [prefix] */
export function tmpDir(prefix = 'gamectl-test-') {
  return fs.mkdtempSync(path.join(os.tmpdir(), prefix));
}

/** A pid that is certainly not running: spawn nothing, pick a huge number. */
export const DEAD_PID = 0x7ffffff0;

/**
 * Run the CLI in-process. Returns {code, out, err, json}.
 * @param {string[]} argv
 * @param {{cwd?: string, signal?: AbortSignal}} [opts]
 */
export async function cli(argv, opts = {}) {
  /** @type {string[]} */
  const out = [];
  /** @type {string[]} */
  const err = [];
  const code = await main(argv, { out: (s) => out.push(s), err: (s) => err.push(s), cwd: opts.cwd, signal: opts.signal });
  const stdout = out.join('\n');
  let json;
  if (argv.includes('--json')) {
    try { json = JSON.parse(stdout); } catch { /* not single JSON */ }
  }
  return { code, out: stdout, err: err.join('\n'), json };
}

/** Run `fn` with env vars set, restoring them afterwards. @param {Record<string, string | undefined>} vars @param {() => Promise<any>} fn */
export async function withEnv(vars, fn) {
  /** @type {Record<string, string | undefined>} */
  const old = {};
  for (const [k, v] of Object.entries(vars)) {
    old[k] = process.env[k];
    if (v === undefined) delete process.env[k];
    else process.env[k] = v;
  }
  try {
    return await fn();
  } finally {
    for (const [k, v] of Object.entries(old)) {
      if (v === undefined) delete process.env[k];
      else process.env[k] = v;
    }
  }
}

/** @param {number} pid */
export function killQuietly(pid) {
  try { process.kill(pid); } catch { /* gone */ }
}

/** @param {() => boolean} cond @param {number} [timeoutMs] */
export async function waitFor(cond, timeoutMs = 5000) {
  const until = Date.now() + timeoutMs;
  while (!cond()) {
    if (Date.now() > until) throw new Error('waitFor timed out');
    await new Promise((r) => setTimeout(r, 25));
  }
}
