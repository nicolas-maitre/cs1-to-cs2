// Builds packages off the main thread so dragging the crop and the canvas stay smooth on big cities.
// The worker parses its own copy of the file once, then answers build requests.

import { readCityFile } from '../core/cs1x.ts';
import { buildPackage } from '../core/plan.ts';
import type { BuildOptions } from '../core/plan.ts';
import { syntheticCity } from '../core/synthetic.ts';
import type { Cs1City } from '../core/types.ts';

export type WorkerRequest =
  | { type: 'load'; bytes?: ArrayBuffer; synthetic?: number; sha256: string | null }
  | { type: 'build'; id: number; opts: BuildOptions };

let city: Cs1City | null = null;

self.onmessage = async (e: MessageEvent<WorkerRequest>) => {
  const m = e.data;
  try {
    if (m.type === 'load') {
      city = m.synthetic ? syntheticCity(m.synthetic) : await readCityFile(new Uint8Array(m.bytes!), { hash: false });
      city.sha256 = m.sha256;
      postMessage({ type: 'loaded' });
    } else if (m.type === 'build') {
      if (!city) throw new Error('no city loaded');
      const t = performance.now();
      const pkg = buildPackage(city, m.opts);
      postMessage({ type: 'built', id: m.id, pkg, ms: performance.now() - t });
    }
  } catch (err) {
    postMessage({ type: 'error', id: m.type === 'build' ? m.id : -1, message: err instanceof Error ? err.message : String(err) });
  }
};
