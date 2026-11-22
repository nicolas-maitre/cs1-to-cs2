// The shared CS2 lock for the terrain and transit scripts (coordinator, 2026-11-22): the same discipline as
// tools/release/acceptance.mjs. Take it with cs2-importer/lib/driver.mjs waitAndTakeLock (never write or take over a
// held lock), and only while no CS2 is running (a game without a lock is someone else's). Release it only once our
// own game has fully exited, so the next owner never finds a CS2 still shutting down under a free lock.
import { waitAndTakeLock, releaseLock } from '../../cs2-importer/lib/driver.mjs';
import { status } from '../../tools/gamectl/lib/index.js';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Waits for the lock and for no CS2 running, then holds the lock as `owner`. */
export async function holdCs2(owner, session, { log = () => {}, timeoutMin = 240 } = {}) {
  const until = Date.now() + timeoutMin * 60e3;
  for (;;) {
    await waitAndTakeLock('cs2', owner, session, { log, timeoutMin: Math.max(1, (until - Date.now()) / 60e3) });
    const st = await status('cs2').catch(() => null);
    if (!st?.running) return;
    releaseLock('cs2', owner);
    if (Date.now() > until) throw new Error('CS2 kept running without a lock');
    log("CS2 is running without a lock (someone else's game); waiting");
    await sleep(15e3);
  }
}

/** Releases our lock once CS2 has exited (up to `waitS` seconds). */
export async function freeCs2(owner, { waitS = 120 } = {}) {
  for (let i = 0; i < waitS / 2; i++) {
    const st = await status('cs2').catch(() => null);
    if (!st?.running) break;
    await sleep(2000);
  }
  releaseLock('cs2', owner);
}
