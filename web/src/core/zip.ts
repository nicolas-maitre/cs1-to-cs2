// The downloadable package as one zip: plan, report, README, Road Builder configs and the terrain files
// (two 32 MB heightmaps, deflated to about a third). Runs in the Web Worker in the UI.

import { strToU8, zipSync } from 'fflate';
import type { Zippable } from 'fflate';
import { buildPackage, completePackage, packageFiles } from './plan.ts';
import type { BuildOptions } from './plan.ts';
import type { Cs1City } from './types.ts';

export function packageZip(city: Cs1City, opts: BuildOptions): { zip: Uint8Array; files: number; terrain: boolean } {
  const pkg = buildPackage(city, opts);
  const binaries = completePackage(city, pkg); // fills plan.terrain before the plan is serialised
  const entries: Zippable = {};
  for (const f of packageFiles(pkg)) entries[f.path] = [strToU8(f.text), { level: 6 }];
  for (const b of binaries) entries[b.path] = [b.data, { level: 6 }];
  return { zip: zipSync(entries), files: Object.keys(entries).length, terrain: binaries.length > 0 };
}
